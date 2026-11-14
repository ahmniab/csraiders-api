using CsRaiders.Api.Application.Common.EmailTemplates.Layouts;
using CsRaiders.Api.Application.Common.EmailTemplates.Models;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.EmailTemplates.Templates;

/// <summary>
/// Email template for password reset instructions and link.
/// </summary>
public sealed class PasswordResetTemplate : IEmailTemplate<PasswordResetTemplateModel>
{
    public const string DefaultSubject = "Reset your CS Raiders password";

    public EmailPurpose Purpose => EmailPurpose.PasswordReset;

    public string BuildSubject(PasswordResetTemplateModel model)
    {
        Validate(model);
        return DefaultSubject;
    }

    public string RenderHtml(PasswordResetTemplateModel model)
    {
        Validate(model);
        var greeting = string.IsNullOrWhiteSpace(model.DisplayName)
            ? "there"
            : HtmlEmailLayout.Sanitize(model.DisplayName);
        var escapedUrl = HtmlEmailLayout.Sanitize(model.ResetUrl);

        var contentHtml = $@"<p>Hi {greeting},</p>
<p>We received a request to reset the password for your CS Raiders account. Click the button below to choose a new password:</p>
<div style=""text-align: center; margin: 24px 0;"">
    <a href=""{escapedUrl}"" style=""display: inline-block; padding: 12px 28px; background-color: #2563eb; color: #ffffff !important; text-decoration: none; font-weight: 600; border-radius: 6px;"">Reset Password</a>
</div>
<p style=""font-size: 14px; color: #64748b;"">If the button does not work, copy and paste the following link into your browser:<br>
<a href=""{escapedUrl}"" style=""color: #2563eb; word-break: break-all;"">{escapedUrl}</a></p>
<p>This link will expire in {model.ExpiryMinutes} minutes. If you did not request a password reset, you can safely ignore this email; your password will remain unchanged.</p>
<p>Best regards,<br>The CS Raiders Team</p>";

        return HtmlEmailLayout.Wrap(DefaultSubject, contentHtml);
    }

    public string RenderText(PasswordResetTemplateModel model)
    {
        Validate(model);
        var greeting = string.IsNullOrWhiteSpace(model.DisplayName)
            ? "there"
            : model.DisplayName;

        return $@"Hi {greeting},

We received a request to reset the password for your CS Raiders account. Use the link below to choose a new password:

{model.ResetUrl}

This link will expire in {model.ExpiryMinutes} minutes. If you did not request a password reset, you can safely ignore this email; your password will remain unchanged.

Best regards,
The CS Raiders Team";
    }

    private static void Validate(PasswordResetTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.ResetUrl))
        {
            throw new ArgumentException("Reset URL cannot be empty.", nameof(model));
        }

        if (model.ExpiryMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(model), "Expiry minutes must be greater than zero.");
        }
    }
}
