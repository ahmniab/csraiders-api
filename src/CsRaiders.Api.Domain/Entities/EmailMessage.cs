namespace CsRaiders.Api.Domain.Entities;

public enum EmailPurpose { RegistrationOtp, PasswordReset, AccountNotice }

public sealed record EmailMessage(string To, string Subject, string Body, EmailPurpose Purpose, string? CorrelationId = null);
