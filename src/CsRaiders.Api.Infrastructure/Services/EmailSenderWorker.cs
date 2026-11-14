using CsRaiders.Api.Application.Common.Events;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CsRaiders.Api.Infrastructure.Services;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("Email dispatched (Console): purpose {Purpose}, recipient {Recipient}",
            message.Purpose, SmtpEmailSender.MaskEmail(message.To));
        return Task.CompletedTask;
    }
}

public sealed class EmailSenderWorker(
    IEmailQueue queue,
    IEmailSender sender,
    IServiceScopeFactory scopeFactory,
    ILogger<EmailSenderWorker> logger,
    IAuthMetricsRecorder metrics,
    IReadOnlyList<TimeSpan>? backoffDelays = null) : BackgroundService
{
    public static readonly IReadOnlyList<TimeSpan> DefaultBackoffDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)];

    private readonly IReadOnlyList<TimeSpan> _delays = backoffDelays ?? DefaultBackoffDelays;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            var succeeded = false;
            Exception? lastException = null;

            for (var attempt = 0; attempt < _delays.Count; attempt++)
            {
                try
                {
                    if (_delays[attempt] > TimeSpan.Zero)
                    {
                        await Task.Delay(_delays[attempt], stoppingToken);
                    }

                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));

                    await sender.SendAsync(message, timeout.Token);
                    metrics.Increment("email_queue.sent");
                    succeeded = true;
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < _delays.Count - 1)
                    {
                        metrics.Increment("email_queue.retry");
                        logger.LogWarning(ex, "Email delivery attempt {Attempt} failed for recipient {Recipient}; retrying...",
                            attempt + 1, SmtpEmailSender.MaskEmail(message.To));
                    }
                }
            }

            if (!succeeded && !stoppingToken.IsCancellationRequested)
            {
                metrics.Increment("email_queue.dead_letter");
                logger.LogError(lastException, "Email delivery permanently failed for recipient {Recipient} after {Attempts} attempts. Moving to dead-letter store.",
                    SmtpEmailSender.MaskEmail(message.To), _delays.Count);

                await HandlePermanentFailureAsync(message, lastException, _delays.Count, stoppingToken);
            }
        }
    }

    private async Task HandlePermanentFailureAsync(
        EmailMessage message,
        Exception? exception,
        int attemptCount,
        CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var deadLetterRepo = scope.ServiceProvider.GetRequiredService<IDeadLetterEmailRepository>();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

            var errorReason = exception is not null
                ? $"{exception.GetType().Name}: {exception.Message}"
                : "Unknown delivery failure";

            var failedAt = DateTimeOffset.UtcNow;

            var deadLetter = DeadLetterEmail.Create(
                recipient: message.To,
                subject: message.Subject,
                purpose: message.Purpose.ToString(),
                errorReason: errorReason,
                attemptCount: attemptCount,
                correlationId: message.CorrelationId,
                failedAt: failedAt);

            await deadLetterRepo.AddAsync(deadLetter, ct);

            var notification = new EmailDeliveryFailedNotification(
                Message: message,
                ErrorReason: errorReason,
                AttemptCount: attemptCount,
                FailedAt: failedAt);

            await publisher.Publish(notification, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist dead letter or publish failure event for recipient {Recipient}",
                SmtpEmailSender.MaskEmail(message.To));
        }
    }
}
