using CsRaiders.Api.Application.Common.EmailTemplates.Layouts;
using CsRaiders.Api.Application.Common.EmailTemplates.Models;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.EmailTemplates.Templates;

/// <summary>
/// Email template for account security notices and activity alerts.
/// </summary>
public sealed class SecurityNoticeTemplate : IEmailTemplate<SecurityNoticeTemplateModel>
{
    public EmailPurpose Purpose => EmailPurpose.AccountNotice;

    public string BuildSubject(SecurityNoticeTemplateModel model)
    {
        Validate(model);
        return $"Security Alert: {model.Title}";
    }

    public string RenderHtml(SecurityNoticeTemplateModel model)
    {
        Validate(model);
        var greeting = string.IsNullOrWhiteSpace(model.DisplayName)
            ? "there"
            : HtmlEmailLayout.Sanitize(model.DisplayName);
        var escapedTitle = HtmlEmailLayout.Sanitize(model.Title);
        var escapedMessage = HtmlEmailLayout.Sanitize(model.Message);
        var formattedTime = model.Timestamp.ToString("u");

        var contentHtml = $@"<p>Hi {greeting},</p>
<div style=""background-color: #fff7ed; border-left: 4px solid #ea580c; padding: 16px; border-radius: 4px; margin: 20px 0;"">
    <h3 style=""margin: 0 0 8px; color: #9a3412;"">{escapedTitle}</h3>
    <p style=""margin: 0; color: #7c2d12;"">{escapedMessage}</p>
</div>
<p style=""font-size: 14px; color: #64748b;"">Timestamp: {formattedTime}</p>
<p>If you did not perform this action or suspect unauthorized activity on your account, please contact support immediately.</p>
<p>Best regards,<br>The CS Raiders Team</p>";

        return HtmlEmailLayout.Wrap(BuildSubject(model), contentHtml);
    }

    public string RenderText(SecurityNoticeTemplateModel model)
    {
        Validate(model);
        var greeting = string.IsNullOrWhiteSpace(model.DisplayName)
            ? "there"
            : model.DisplayName;
        var formattedTime = model.Timestamp.ToString("u");

        return $@"Hi {greeting},

Security Alert: {model.Title}

{model.Message}

Timestamp: {formattedTime}

If you did not perform this action or suspect unauthorized activity on your account, please contact support immediately.

Best regards,
The CS Raiders Team";
    }

    private static void Validate(SecurityNoticeTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            throw new ArgumentException("Title cannot be empty.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(model.Message))
        {
            throw new ArgumentException("Message cannot be empty.", nameof(model));
        }
    }
}
