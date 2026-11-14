namespace CsRaiders.Api.Application.Common.EmailTemplates.Models;

/// <summary>
/// Parameter model for registration verification OTP email.
/// </summary>
/// <param name="Otp">The one-time verification code.</param>
/// <param name="DisplayName">The user's display name or nickname.</param>
/// <param name="ExpiryMinutes">Validity duration in minutes (default 15 minutes).</param>
public sealed record RegistrationOtpTemplateModel(
    string Otp,
    string DisplayName,
    int ExpiryMinutes = 15);
