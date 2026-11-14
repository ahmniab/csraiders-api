using System.Threading.Channels;
using CsRaiders.Api.Application.Common.Events;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Infrastructure.DependencyInjection;
using CsRaiders.Api.Infrastructure.Options;
using CsRaiders.Api.Infrastructure.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

namespace CsRaiders.Api.IntegrationTests;

public sealed class EmailDeliveryIntegrationTests
{
    [Fact]
    public void Dependency_injection_resolves_smtp_sender_when_delivery_mode_is_smtp()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=test;Username=postgres;Password=postgres",
                ["Email:DeliveryMode"] = "Smtp",
                ["Email:Smtp:Host"] = "mail.example.com",
                ["Email:Smtp:Port"] = "587",
                ["Email:Smtp:SenderEmail"] = "no-reply@csraiders.com",
                ["Email:Smtp:SenderName"] = "CS Raiders"
            })
            .Build();

        services.AddLogging();
        services.AddInfrastructureServices(config);

        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();
        var deadLetterRepo = provider.GetRequiredService<IDeadLetterEmailRepository>();
        var emailOptions = provider.GetRequiredService<IOptions<EmailOptions>>().Value;

        Assert.IsType<SmtpEmailSender>(sender);
        Assert.NotNull(deadLetterRepo);
        Assert.Equal("Smtp", emailOptions.DeliveryMode);
        Assert.Equal("mail.example.com", emailOptions.Smtp.Host);
    }

    [Fact]
    public void Dependency_injection_resolves_console_sender_when_delivery_mode_is_console()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=test;Username=postgres;Password=postgres",
                ["Email:DeliveryMode"] = "Console"
            })
            .Build();

        services.AddLogging();
        services.AddInfrastructureServices(config);

        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();

        Assert.IsType<ConsoleEmailSender>(sender);
    }

    [Fact]
    public async Task End_to_end_smtp_dispatch_builds_and_transmits_multipart_email()
    {
        var options = new EmailOptions
        {
            DeliveryMode = "Smtp",
            Smtp = new SmtpOptions
            {
                Host = "smtp.mailtrap.io",
                Port = 587,
                UseSsl = true,
                Username = "user123",
                Password = "pwd123",
                SenderEmail = "system@csraiders.com",
                SenderName = "CS Raiders Platform",
                TimeoutSeconds = 15
            }
        };

        var sender = new RecordingSmtpSender(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<SmtpEmailSender>.Instance);

        var message = new EmailMessage(
            "student@academy.com",
            "Verification Code",
            "<h1>654321</h1>",
            "654321",
            EmailPurpose.RegistrationOtp,
            "corr-e2e-001");

        await sender.SendAsync(message, CancellationToken.None);

        Assert.Equal("smtp.mailtrap.io", sender.ConnectedHost);
        Assert.Equal(587, sender.ConnectedPort);
        Assert.Equal(SecureSocketOptions.StartTls, sender.ConnectedOptions);
        Assert.Equal("user123", sender.AuthenticatedUser);
        Assert.NotNull(sender.SentMessage);

        var sent = sender.SentMessage!;
        Assert.Equal("Verification Code", sent.Subject);
        Assert.Equal("\"CS Raiders Platform\" <system@csraiders.com>", sent.From.ToString());
        Assert.Equal("student@academy.com", sent.To.ToString());
        Assert.Equal("<h1>654321</h1>", sent.HtmlBody);
        Assert.Equal("654321", sent.TextBody);
        Assert.Equal("corr-e2e-001", sent.Headers["X-Correlation-ID"]);
        Assert.Equal(EmailPurpose.RegistrationOtp.ToString(), sent.Headers["X-Email-Purpose"]);
    }

    [Fact]
    public async Task End_to_end_worker_dead_letter_persistence_and_event_publishing()
    {
        var queue = new TestAsyncQueue();
        var failingSender = new PermanentFailSender();
        var inMemoryRepo = new InMemoryDeadLetterRepo();
        var inMemoryPublisher = new InMemoryPublisher();
        var metrics = new TestAuthMetrics();

        var services = new ServiceCollection();
        services.AddSingleton<IDeadLetterEmailRepository>(inMemoryRepo);
        services.AddSingleton<IPublisher>(inMemoryPublisher);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new EmailSenderWorker(
            queue,
            failingSender,
            scopeFactory,
            NullLogger<EmailSenderWorker>.Instance,
            metrics,
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(5)]);

        var message = new EmailMessage(
            "exhausted@test.com",
            "Critical Alert",
            "<p>Alert body</p>",
            "Alert body",
            EmailPurpose.AccountNotice,
            "corr-fail-999");

        await queue.EnqueueAsync(message);
        queue.Complete();

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // 1. Retry metrics: 2 retries, 1 dead letter
        Assert.Equal(2, metrics.GetCount("email_queue.retry"));
        Assert.Equal(1, metrics.GetCount("email_queue.dead_letter"));

        // 2. Dead-letter repo persistence
        Assert.Single(inMemoryRepo.Stored);
        var deadLetter = inMemoryRepo.Stored[0];
        Assert.Equal("exhausted@test.com", deadLetter.Recipient);
        Assert.Equal("Critical Alert", deadLetter.Subject);
        Assert.Equal("AccountNotice", deadLetter.Purpose);
        Assert.Equal(3, deadLetter.AttemptCount);
        Assert.Equal("corr-fail-999", deadLetter.CorrelationId);
        Assert.Contains("Simulated connection timeout", deadLetter.ErrorReason);

        // 3. Event publishing
        Assert.Single(inMemoryPublisher.Notifications);
        var evt = Assert.IsType<EmailDeliveryFailedNotification>(inMemoryPublisher.Notifications[0]);
        Assert.Equal(message, evt.Message);
        Assert.Equal(3, evt.AttemptCount);
        Assert.Contains("Simulated connection timeout", evt.ErrorReason);
    }

    private sealed class RecordingSmtpSender(
        IOptions<EmailOptions> options,
        Microsoft.Extensions.Logging.ILogger<SmtpEmailSender> logger)
        : SmtpEmailSender(options, logger)
    {
        public string? ConnectedHost { get; private set; }
        public int ConnectedPort { get; private set; }
        public SecureSocketOptions ConnectedOptions { get; private set; }
        public string? AuthenticatedUser { get; private set; }
        public MimeMessage? SentMessage { get; private set; }

        protected override Task ConnectClientAsync(ISmtpClient client, string host, int port, SecureSocketOptions options, CancellationToken ct)
        {
            ConnectedHost = host;
            ConnectedPort = port;
            ConnectedOptions = options;
            return Task.CompletedTask;
        }

        protected override Task AuthenticateClientAsync(ISmtpClient client, string username, string password, CancellationToken ct)
        {
            AuthenticatedUser = username;
            return Task.CompletedTask;
        }

        protected override Task SendMessageAsync(ISmtpClient client, MimeMessage mime, CancellationToken ct)
        {
            SentMessage = mime;
            return Task.CompletedTask;
        }

        protected override Task DisconnectClientAsync(ISmtpClient client, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class PermanentFailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default) =>
            throw new TimeoutException("Simulated connection timeout to remote SMTP server");
    }

    private sealed class InMemoryDeadLetterRepo : IDeadLetterEmailRepository
    {
        public List<DeadLetterEmail> Stored { get; } = [];

        public Task AddAsync(DeadLetterEmail deadLetter, CancellationToken ct = default)
        {
            Stored.Add(deadLetter);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DeadLetterEmail>> GetRecentAsync(int count = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeadLetterEmail>>(Stored.Take(count).ToList());
    }

    private sealed class InMemoryPublisher : IPublisher
    {
        public List<INotification> Notifications { get; } = [];

        public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Notifications.Add(notification);
            return ValueTask.CompletedTask;
        }

        public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n) Notifications.Add(n);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAsyncQueue : IEmailQueue
    {
        private readonly Channel<EmailMessage> _channel = Channel.CreateUnbounded<EmailMessage>();
        public bool TryEnqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
        public ValueTask EnqueueAsync(EmailMessage message) => _channel.Writer.WriteAsync(message);
        public void Complete() => _channel.Writer.Complete();
        public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct = default) => _channel.Reader.ReadAllAsync(ct);
    }

    private sealed class TestAuthMetrics : IAuthMetricsRecorder
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
