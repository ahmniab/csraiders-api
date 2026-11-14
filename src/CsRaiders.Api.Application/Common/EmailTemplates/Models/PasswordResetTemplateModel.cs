namespace CsRaiders.Api.Application.Common.EmailTemplates.Models;

/// <summary>
/// Parameter model for password reset email.
/// </summary>
/// <param name="ResetUrl">The password reset link or token URL.</param>
/// <param name="DisplayName">The user's display name or nickname.</param>
/// <param name="ExpiryMinutes">Validity duration in minutes (default 60 minutes).</param>
public sealed record PasswordResetTemplateModel(
    string ResetUrl,
    string DisplayName,
    int ExpiryMinutes = 60);
