using System.Threading.Channels;
using CsRaiders.Api.Application.Common.Events;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Infrastructure.Services;
using FluentAssertions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CsRaiders.Api.Application.UnitTests.Security;

public class EmailSenderWorkerTests
{
    [Fact]
    public void Default_backoff_delays_match_specification()
    {
        EmailSenderWorker.DefaultBackoffDelays.Should().Equal(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Worker_sends_email_successfully_on_first_attempt()
    {
        var queue = new TestQueue();
        var sender = new TestSender(succeedAfterAttempts: 1);
        var (scopeFactory, _, _) = CreateScopeFactory();
        var metrics = new TestMetrics();

        var worker = new EmailSenderWorker(
            queue,
            sender,
            scopeFactory,
            NullLogger<EmailSenderWorker>.Instance,
            metrics,
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10)]);

        var message = new EmailMessage("user@test.com", "Test Subject", "<p>body</p>", "body", EmailPurpose.RegistrationOtp);
        await queue.EnqueueAsync(message);
        queue.Complete();

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        sender.Attempts.Should().Be(1);
        metrics.GetCount("email_queue.sent").Should().Be(1);
        metrics.GetCount("email_queue.retry").Should().Be(0);
        metrics.GetCount("email_queue.dead_letter").Should().Be(0);
    }

    [Fact]
    public async Task Worker_retries_transient_failure_and_succeeds()
    {
        var queue = new TestQueue();
        var sender = new TestSender(succeedAfterAttempts: 2); // Fails 1st, succeeds 2nd
        var (scopeFactory, _, _) = CreateScopeFactory();
        var metrics = new TestMetrics();

        var worker = new EmailSenderWorker(
            queue,
            sender,
            scopeFactory,
            NullLogger<EmailSenderWorker>.Instance,
            metrics,
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10)]);

        var message = new EmailMessage("user@test.com", "Retry Subject", "<p>body</p>", "body", EmailPurpose.PasswordReset);
        await queue.EnqueueAsync(message);
        queue.Complete();

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        sender.Attempts.Should().Be(2);
        metrics.GetCount("email_queue.retry").Should().Be(1);
        metrics.GetCount("email_queue.sent").Should().Be(1);
        metrics.GetCount("email_queue.dead_letter").Should().Be(0);
    }

    [Fact]
    public async Task Worker_persists_to_dead_letter_and_emits_event_on_exhaustion()
    {
        var queue = new TestQueue();
        var sender = new TestSender(succeedAfterAttempts: 99); // Always fails
        var (scopeFactory, deadLetterRepo, publisher) = CreateScopeFactory();
        var metrics = new TestMetrics();

        var worker = new EmailSenderWorker(
            queue,
            sender,
            scopeFactory,
            NullLogger<EmailSenderWorker>.Instance,
            metrics,
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10)]);

        var message = new EmailMessage(
            "student@fail.com",
            "Exhaustion Subject",
            "<h1>Fail</h1>",
            "Fail",
            EmailPurpose.RegistrationOtp,
            "corr-exhausted");

        await queue.EnqueueAsync(message);
        queue.Complete();

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        sender.Attempts.Should().Be(3);
        metrics.GetCount("email_queue.retry").Should().Be(2);
        metrics.GetCount("email_queue.dead_letter").Should().Be(1);
        metrics.GetCount("email_queue.sent").Should().Be(0);

        // Verify dead-letter storage
        deadLetterRepo.Stored.Should().ContainSingle();
        var deadLetter = deadLetterRepo.Stored[0];
        deadLetter.Recipient.Should().Be("student@fail.com");
        deadLetter.Subject.Should().Be("Exhaustion Subject");
        deadLetter.Purpose.Should().Be(EmailPurpose.RegistrationOtp.ToString());
        deadLetter.AttemptCount.Should().Be(3);
        deadLetter.CorrelationId.Should().Be("corr-exhausted");
        deadLetter.ErrorReason.Should().Contain("Simulated SMTP delivery failure");

        // Verify event publishing
        publisher.Published.Should().ContainSingle();
        var notification = publisher.Published[0].Should().BeOfType<EmailDeliveryFailedNotification>().Subject;
        notification.Message.Should().Be(message);
        notification.AttemptCount.Should().Be(3);
        notification.ErrorReason.Should().Contain("Simulated SMTP delivery failure");
    }

    [Fact]
    public async Task Worker_continues_processing_subsequent_messages_after_dead_letter()
    {
        var queue = new TestQueue();
        var sender = new AlternatingSender(); // Fails message 1, succeeds message 2
        var (scopeFactory, deadLetterRepo, _) = CreateScopeFactory();
        var metrics = new TestMetrics();

        var worker = new EmailSenderWorker(
            queue,
            sender,
            scopeFactory,
            NullLogger<EmailSenderWorker>.Instance,
            metrics,
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10)]);

        var msg1 = new EmailMessage("bad@test.com", "Bad", "bad", "bad", EmailPurpose.AccountNotice);
        var msg2 = new EmailMessage("good@test.com", "Good", "good", "good", EmailPurpose.AccountNotice);

        await queue.EnqueueAsync(msg1);
        await queue.EnqueueAsync(msg2);
        queue.Complete();

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        metrics.GetCount("email_queue.dead_letter").Should().Be(1);
        metrics.GetCount("email_queue.sent").Should().Be(1);
        deadLetterRepo.Stored.Should().ContainSingle(d => d.Recipient == "bad@test.com");
    }

    private static (IServiceScopeFactory ScopeFactory, InMemoryDeadLetterRepo Repo, InMemoryPublisher Publisher) CreateScopeFactory()
    {
        var services = new ServiceCollection();
        var repo = new InMemoryDeadLetterRepo();
        var publisher = new InMemoryPublisher();

        services.AddSingleton<IDeadLetterEmailRepository>(repo);
        services.AddSingleton<IPublisher>(publisher);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), repo, publisher);
    }

    private sealed class TestQueue : IEmailQueue
    {
        private readonly Channel<EmailMessage> _channel = Channel.CreateUnbounded<EmailMessage>();
        public bool TryEnqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
        public ValueTask EnqueueAsync(EmailMessage message) => _channel.Writer.WriteAsync(message);
        public void Complete() => _channel.Writer.Complete();
        public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct = default) => _channel.Reader.ReadAllAsync(ct);
    }

    private sealed class TestSender(int succeedAfterAttempts) : IEmailSender
    {
        public int Attempts { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Attempts++;
            if (Attempts < succeedAfterAttempts)
            {
                throw new InvalidOperationException($"Simulated SMTP delivery failure on attempt {Attempts}");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class AlternatingSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (message.To.StartsWith("bad", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Failed bad email");
            }
            return Task.CompletedTask;
        }
    }

    public sealed class InMemoryDeadLetterRepo : IDeadLetterEmailRepository
    {
        public List<DeadLetterEmail> Stored { get; } = [];

        public Task AddAsync(DeadLetterEmail deadLetter, CancellationToken ct = default)
        {
            Stored.Add(deadLetter);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DeadLetterEmail>> GetRecentAsync(int count = 50, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<DeadLetterEmail>>(Stored.Take(count).ToList());
        }
    }

    public sealed class InMemoryPublisher : IPublisher
    {
        public List<INotification> Published { get; } = [];

        public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification);
            return ValueTask.CompletedTask;
        }

        public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n)
            {
                Published.Add(n);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestMetrics : IAuthMetricsRecorder
    {
        private readonly Dictionary<string, long> _counters = [];
        public void Increment(string name)
        {
            lock (_counters)
            {
                _counters[name] = _counters.GetValueOrDefault(name) + 1;
            }
        }

        public long GetCount(string name)
        {
            lock (_counters)
            {
                return _counters.GetValueOrDefault(name);
            }
        }

        public IReadOnlyDictionary<string, long> Snapshot()
        {
            lock (_counters)
            {
                return new Dictionary<string, long>(_counters);
            }
        }
    }
}
