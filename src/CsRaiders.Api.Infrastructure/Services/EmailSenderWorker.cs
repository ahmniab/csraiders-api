using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
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
        logger.LogInformation("Email queued: purpose {Purpose}, recipient redacted", message.Purpose);
        return Task.CompletedTask;
    }
}

public sealed class EmailSenderWorker(IEmailQueue queue, IEmailSender sender, ILogger<EmailSenderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5) };
            for (var attempt = 0; attempt < delays.Length; attempt++)
            {
                try
                {
                    if (delays[attempt] > TimeSpan.Zero) await Task.Delay(delays[attempt], stoppingToken);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await sender.SendAsync(message, timeout.Token);
                    break;
                }
                catch (Exception ex) when (attempt < delays.Length - 1)
                {
                    logger.LogWarning(ex, "Email delivery attempt {Attempt} failed; recipient redacted", attempt + 1);
                }
            }
        }
    }
}
