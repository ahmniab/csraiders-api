using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Infrastructure.Options;
using CsRaiders.Api.Infrastructure.Services;
using FluentAssertions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CsRaiders.Api.Application.UnitTests.Security;

public class SmtpEmailSenderTests
{
    private static SmtpOptions CreateOptions(
        string host = "mail.example.com",
        int port = 587,
        bool useSsl = true,
        string? username = "apiuser",
        string? password = "secretpassword",
        string senderEmail = "no-reply@csraiders.com",
        string senderName = "CS Raiders",
        int timeoutSeconds = 10) => new()
    {
        Host = host,
        Port = port,
        UseSsl = useSsl,
        Username = username,
        Password = password,
        SenderEmail = senderEmail,
        SenderName = senderName,
        TimeoutSeconds = timeoutSeconds
    };

    [Theory]
    [InlineData("john.doe@example.com", "j***e@example.com")]
    [InlineData("alex@csraiders.org", "a***x@csraiders.org")]
    [InlineData("ab@domain.com", "a*@domain.com")]
    [InlineData("a@domain.com", "*@domain.com")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("invalid-email", "***")]
    public void MaskEmail_redacts_sensitive_recipient_details(string? input, string expected)
    {
        SmtpEmailSender.MaskEmail(input).Should().Be(expected);
    }

    [Fact]
    public void BuildMimeMessage_constructs_valid_multipart_message()
    {
        var options = CreateOptions();
        var message = new EmailMessage(
            "student@university.edu",
            "Verify your email",
            "<h1>Verify</h1><p>123456</p>",
            "Verify: 123456",
            EmailPurpose.RegistrationOtp,
            "corr-999");

        var mime = SmtpEmailSender.BuildMimeMessage(message, options);

        mime.From.Mailboxes.Should().ContainSingle(m => m.Address == options.SenderEmail && m.Name == options.SenderName);
        mime.To.Mailboxes.Should().ContainSingle(m => m.Address == "student@university.edu");
        mime.Subject.Should().Be("Verify your email");
        mime.Headers["X-Correlation-ID"].Should().Be("corr-999");
        mime.Headers["X-Email-Purpose"].Should().Be(EmailPurpose.RegistrationOtp.ToString());

        mime.HtmlBody.Should().Be("<h1>Verify</h1><p>123456</p>");
        mime.TextBody.Should().Be("Verify: 123456");
    }

    [Fact]
    public void BuildMimeMessage_throws_when_recipient_is_empty()
    {
        var options = CreateOptions();
        var message = new EmailMessage("", "Subject", "Body", EmailPurpose.AccountNotice);

        var act = () => SmtpEmailSender.BuildMimeMessage(message, options);

        act.Should().Throw<ArgumentException>().WithMessage("*Recipient address cannot be empty.*");
    }

    [Fact]
    public async Task SendAsync_connects_authenticates_sends_and_disconnects()
    {
        var options = CreateOptions(username: "myuser", password: "mypassword", timeoutSeconds: 12);
        var sender = new TestableSmtpSender(
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { Smtp = options }),
            NullLogger<SmtpEmailSender>.Instance);

        var message = new EmailMessage(
            "student@example.com",
            "Hello",
            "<p>Test</p>",
            "Test",
            EmailPurpose.AccountNotice);

        await sender.SendAsync(message, CancellationToken.None);

        sender.ConnectedHost.Should().Be("mail.example.com");
        sender.ConnectedPort.Should().Be(587);
        sender.ConnectedOptions.Should().Be(SecureSocketOptions.StartTls);
        sender.AuthenticatedUsername.Should().Be("myuser");
        sender.AuthenticatedPassword.Should().Be("mypassword");
        sender.SentMimeMessage.Should().NotBeNull();
        sender.SentMimeMessage!.Subject.Should().Be("Hello");
        sender.Disconnected.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_skips_authentication_when_credentials_omitted()
    {
        var options = CreateOptions(useSsl: false, username: null, password: null);
        var sender = new TestableSmtpSender(
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { Smtp = options }),
            NullLogger<SmtpEmailSender>.Instance);

        var message = new EmailMessage(
            "student@example.com",
            "Hello",
            "<p>Test</p>",
            "Test",
            EmailPurpose.AccountNotice);

        await sender.SendAsync(message, CancellationToken.None);

        sender.ConnectedOptions.Should().Be(SecureSocketOptions.None);
        sender.AuthenticatedUsername.Should().BeNull();
        sender.SentMimeMessage.Should().NotBeNull();
        sender.Disconnected.Should().BeTrue();
    }

    private sealed class TestableSmtpSender(
        IOptions<EmailOptions> options,
        Microsoft.Extensions.Logging.ILogger<SmtpEmailSender> logger)
        : SmtpEmailSender(options, logger)
    {
        public string? ConnectedHost { get; private set; }
        public int ConnectedPort { get; private set; }
        public SecureSocketOptions ConnectedOptions { get; private set; }
        public string? AuthenticatedUsername { get; private set; }
        public string? AuthenticatedPassword { get; private set; }
        public MimeMessage? SentMimeMessage { get; private set; }
        public bool Disconnected { get; private set; }

        protected override Task ConnectClientAsync(ISmtpClient client, string host, int port, SecureSocketOptions options, CancellationToken ct)
        {
            ConnectedHost = host;
            ConnectedPort = port;
            ConnectedOptions = options;
            return Task.CompletedTask;
        }

        protected override Task AuthenticateClientAsync(ISmtpClient client, string username, string password, CancellationToken ct)
        {
            AuthenticatedUsername = username;
            AuthenticatedPassword = password;
            return Task.CompletedTask;
        }

        protected override Task SendMessageAsync(ISmtpClient client, MimeMessage mime, CancellationToken ct)
        {
            SentMimeMessage = mime;
            return Task.CompletedTask;
        }

        protected override Task DisconnectClientAsync(ISmtpClient client, CancellationToken ct)
        {
            Disconnected = true;
            return Task.CompletedTask;
        }
    }
}
