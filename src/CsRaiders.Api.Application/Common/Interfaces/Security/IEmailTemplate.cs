using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Security;

/// <summary>
/// Defines a strongly typed strategy for generating email subject, HTML, and plain-text body content.
/// </summary>
/// <typeparam name="TModel">The template parameter model type.</typeparam>
public interface IEmailTemplate<in TModel>
{
    /// <summary>
    /// Gets the business purpose of this email.
    /// </summary>
    EmailPurpose Purpose { get; }

    /// <summary>
    /// Builds the subject line for the email using the provided model.
    /// </summary>
    string BuildSubject(TModel model);

    /// <summary>
    /// Renders the rich HTML body with all dynamic parameters sanitized.
    /// </summary>
    string RenderHtml(TModel model);

    /// <summary>
    /// Renders the accessible plain-text fallback body.
    /// </summary>
    string RenderText(TModel model);
}
