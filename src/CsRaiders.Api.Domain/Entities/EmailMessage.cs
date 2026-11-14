namespace CsRaiders.Api.Domain.Entities;

public enum EmailPurpose { RegistrationOtp, PasswordReset, AccountNotice }

public sealed record EmailMessage(
    string To,
    string Subject,
    string BodyHtml,
    string BodyText,
    EmailPurpose Purpose,
    string? CorrelationId = null)
{
    public string Body => BodyHtml;

    public EmailMessage(
        string To,
        string Subject,
        string Body,
        EmailPurpose Purpose,
        string? CorrelationId = null)
        : this(To, Subject, Body, Body, Purpose, CorrelationId)
    {
    }
}
