using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Infrastructure.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CsRaiders.Api.Infrastructure.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly Func<ISmtpClient> _clientFactory;

    public SmtpEmailSender(
        IOptions<EmailOptions> options,
        ILogger<SmtpEmailSender> logger,
        Func<ISmtpClient>? clientFactory = null)
    {
        _options = options.Value.Smtp;
        _logger = logger;
        _clientFactory = clientFactory ?? (() => new SmtpClient());
    }

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
        {
            return "***";
        }

        var local = email[..atIndex];
        var domain = email[(atIndex + 1)..];

        if (local.Length <= 1)
        {
            return $"*@{domain}";
        }
        if (local.Length == 2)
        {
            return $"{local[0]}*@{domain}";
        }

        return $"{local[0]}***{local[^1]}@{domain}";
    }

    public static MimeMessage BuildMimeMessage(EmailMessage message, SmtpOptions options)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(message.To))
        {
            throw new ArgumentException("Recipient address cannot be empty.", nameof(message));
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.SenderName, options.SenderEmail));
        mime.To.Add(MailboxAddress.Parse(message.To.Trim()));
        mime.Subject = message.Subject ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            mime.Headers.Add("X-Correlation-ID", message.CorrelationId);
        }
        mime.Headers.Add("X-Email-Purpose", message.Purpose.ToString());

        var builder = new BodyBuilder
        {
            TextBody = message.BodyText,
            HtmlBody = message.BodyHtml
        };

        mime.Body = builder.ToMessageBody();
        return mime;
    }

    public virtual async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mime = BuildMimeMessage(message, _options);

        using var client = _clientFactory();
        client.Timeout = _options.TimeoutSeconds * 1000;

        var secureSocketOptions = !_options.UseSsl
            ? SecureSocketOptions.None
            : _options.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

        await ConnectClientAsync(client, _options.Host, _options.Port, secureSocketOptions, ct);

        if (!string.IsNullOrWhiteSpace(_options.Username) && !string.IsNullOrWhiteSpace(_options.Password))
        {
            await AuthenticateClientAsync(client, _options.Username, _options.Password, ct);
        }

        await SendMessageAsync(client, mime, ct);
        await DisconnectClientAsync(client, ct);

        _logger.LogInformation(
            "Email dispatched via SMTP to {Recipient} (purpose: {Purpose}, correlationId: {CorrelationId})",
            MaskEmail(message.To), message.Purpose, message.CorrelationId);
    }

    protected virtual Task ConnectClientAsync(ISmtpClient client, string host, int port, SecureSocketOptions options, CancellationToken ct) =>
        client.ConnectAsync(host, port, options, ct);

    protected virtual Task AuthenticateClientAsync(ISmtpClient client, string username, string password, CancellationToken ct) =>
        client.AuthenticateAsync(username, password, ct);

    protected virtual Task SendMessageAsync(ISmtpClient client, MimeMessage mime, CancellationToken ct) =>
        client.SendAsync(mime, ct);

    protected virtual Task DisconnectClientAsync(ISmtpClient client, CancellationToken ct) =>
        client.DisconnectAsync(true, ct);
}
