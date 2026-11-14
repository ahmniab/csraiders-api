using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Infrastructure.Services;

/// <summary>
/// Unified implementation of ITemplatedEmailQueue delegating template composition to
/// IEmailTemplateService and non-blocking channel queuing to IEmailQueue.
/// </summary>
public sealed class TemplatedEmailQueue(
    IEmailQueue emailQueue,
    IEmailTemplateService templateService) : ITemplatedEmailQueue
{
    public bool TryEnqueueTemplate<TModel>(
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null)
    {
        return emailQueue.TryEnqueueTemplate(templateService, recipient, template, model, correlationId);
    }

    public bool TryEnqueue(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return emailQueue.TryEnqueue(message);
    }
}
