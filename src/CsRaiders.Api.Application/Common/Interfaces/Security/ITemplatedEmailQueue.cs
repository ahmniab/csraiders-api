using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Security;

/// <summary>
/// Provides extension methods for atomic template rendering and non-blocking delivery queueing.
/// </summary>
public static class TemplatedEmailQueueExtensions
{
    /// <summary>
    /// Renders an email template and enqueues the resulting message into the delivery queue in a single atomic call.
    /// Returns false immediately if the queue has reached capacity without blocking the calling thread.
    /// </summary>
    /// <typeparam name="TModel">The template parameter model type.</typeparam>
    /// <param name="queue">The underlying email queue.</param>
    /// <param name="templateService">The template rendering service.</param>
    /// <param name="recipient">The target recipient email address.</param>
    /// <param name="template">The email template definition.</param>
    /// <param name="model">The template model parameters.</param>
    /// <param name="correlationId">Optional correlation tracking identifier.</param>
    /// <returns>True if the message was accepted into the queue; false if saturated.</returns>
    public static bool TryEnqueueTemplate<TModel>(
        this IEmailQueue queue,
        IEmailTemplateService templateService,
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(templateService);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(model);

        var message = templateService.CreateMessage(recipient, template, model, correlationId);
        return queue.TryEnqueue(message);
    }
}

/// <summary>
/// Unified facade combining email template rendering and background delivery queueing.
/// </summary>
public interface ITemplatedEmailQueue
{
    /// <summary>
    /// Renders an email template and enqueues the resulting message in a single atomic call.
    /// Returns false immediately if the queue has reached capacity without blocking the calling thread.
    /// </summary>
    /// <typeparam name="TModel">The template parameter model type.</typeparam>
    /// <param name="recipient">The target recipient email address.</param>
    /// <param name="template">The email template definition.</param>
    /// <param name="model">The template model parameters.</param>
    /// <param name="correlationId">Optional correlation tracking identifier.</param>
    /// <returns>True if the message was accepted into the queue; false if saturated.</returns>
    bool TryEnqueueTemplate<TModel>(
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null);

    /// <summary>
    /// Enqueues an already composed EmailMessage into the delivery queue.
    /// Returns false immediately if the queue has reached capacity without blocking the calling thread.
    /// </summary>
    /// <param name="message">The email message to enqueue.</param>
    /// <returns>True if accepted; false if saturated.</returns>
    bool TryEnqueue(EmailMessage message);
}
