using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Infrastructure.Services;

/// <summary>
/// Service responsible for rendering email templates and composing EmailMessage instances.
/// </summary>
public sealed class EmailTemplateService : IEmailTemplateService
{
    public EmailContent Render<TModel>(IEmailTemplate<TModel> template, TModel model)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(model);

        var subject = template.BuildSubject(model);
        var bodyHtml = template.RenderHtml(model);
        var bodyText = template.RenderText(model);

        return new EmailContent(subject, bodyHtml, bodyText);
    }

    public EmailMessage CreateMessage<TModel>(
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            throw new ArgumentException("Recipient email address cannot be empty.", nameof(recipient));
        }

        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(model);

        // Validate and normalize recipient address (trims and converts to lowercase)
        var normalizedRecipient = Email.Create(recipient).Value;
        var content = Render(template, model);

        return new EmailMessage(
            normalizedRecipient,
            content.Subject,
            content.BodyHtml,
            content.BodyText,
            template.Purpose,
            correlationId);
    }
}
