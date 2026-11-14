using CsRaiders.Api.Application.Common.EmailTemplates.Layouts;
using CsRaiders.Api.Application.Common.EmailTemplates.Models;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.EmailTemplates.Templates;

/// <summary>
/// Email template for user registration one-time verification code.
/// </summary>
public sealed class RegistrationOtpTemplate : IEmailTemplate<RegistrationOtpTemplateModel>
{
    public const string DefaultSubject = "Verify your CS Raiders account";

    public EmailPurpose Purpose => EmailPurpose.RegistrationOtp;

    public string BuildSubject(RegistrationOtpTemplateModel model)
    {
        Validate(model);
        return DefaultSubject;
    }

    public string RenderHtml(RegistrationOtpTemplateModel model)
    {
        Validate(model);
        var greeting = string.IsNullOrWhiteSpace(model.DisplayName)
            ? "there"
            : HtmlEmailLayout.Sanitize(model.DisplayName);
        var escapedOtp = HtmlEmailLayout.Sanitize(model.Otp);

        var contentHtml = $@"<p>Hi {greeting},</p>
<p>Thank you for registering with CS Raiders. Please use the verification code below to complete your registration:</p>
<div style=""font-family: Consolas, 'Courier New', Courier, monospace; font-size: 32px; letter-spacing: 6px; font-weight: 700; color: #0f172a; background-color: #f1f5f9; padding: 16px 24px; border-radius: 8px; text-align: center; margin: 24px 0; border: 1px dashed #94a3b8;"">
    {escapedOtp}
</div>
<p>This code will expire in {model.ExpiryMinutes} minutes. If you did not request this registration, please ignore this email.</p>
<p>Best regards,<br>The CS Raiders Team</p>";

        return HtmlEmailLayout.Wrap(DefaultSubject, contentHtml);
    }

    public string RenderText(RegistrationOtpTemplateModel model)
    {
        Validate(model);
        var greeting = string.IsNullOrWhiteSpace(model.DisplayName)
            ? "there"
            : model.DisplayName;

        return $@"Hi {greeting},

Thank you for registering with CS Raiders. Please use the verification code below to complete your registration:

{model.Otp}

This code will expire in {model.ExpiryMinutes} minutes. If you did not request this registration, please ignore this email.

Best regards,
The CS Raiders Team";
    }

    private static void Validate(RegistrationOtpTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Otp))
        {
            throw new ArgumentException("OTP code cannot be empty.", nameof(model));
        }

        if (model.ExpiryMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(model), "Expiry minutes must be greater than zero.");
        }
    }
}
