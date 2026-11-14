using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Security;

/// <summary>
/// Encapsulates composed email content containing subject, rich HTML body, and plain-text body.
/// </summary>
public sealed record EmailContent(string Subject, string BodyHtml, string BodyText);

/// <summary>
/// Service responsible for rendering email templates and composing full EmailMessage payloads.
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Renders an email template with the specified model into subject, HTML, and text content.
    /// </summary>
    EmailContent Render<TModel>(IEmailTemplate<TModel> template, TModel model);

    /// <summary>
    /// Composes a complete, validated EmailMessage ready for asynchronous queuing and dispatch.
    /// </summary>
    EmailMessage CreateMessage<TModel>(
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null);
}
