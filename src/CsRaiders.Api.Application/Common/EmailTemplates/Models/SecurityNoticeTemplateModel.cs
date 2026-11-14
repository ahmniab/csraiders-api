namespace CsRaiders.Api.Application.Common.EmailTemplates.Models;

/// <summary>
/// Parameter model for account security notice and alert emails.
/// </summary>
/// <param name="Title">The notice title or event description (e.g. New login from unknown device).</param>
/// <param name="Message">The detailed notification body explaining the security action.</param>
/// <param name="DisplayName">The user's display name or nickname.</param>
/// <param name="Timestamp">The timestamp when the security event occurred.</param>
public sealed record SecurityNoticeTemplateModel(
    string Title,
    string Message,
    string DisplayName,
    DateTimeOffset Timestamp);
