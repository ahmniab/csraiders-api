using CsRaiders.Api.Domain.Common;

namespace CsRaiders.Api.Domain.Entities;

public sealed class DeadLetterEmail : Entity
{
    private DeadLetterEmail() { }

    public string Recipient { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Purpose { get; private set; } = null!;
    public string ErrorReason { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset FailedAt { get; private set; }

    public static DeadLetterEmail Create(
        string recipient,
        string subject,
        string purpose,
        string errorReason,
        int attemptCount,
        string? correlationId,
        DateTimeOffset failedAt,
        Guid? id = null)
    {
        var entity = new DeadLetterEmail
        {
            Recipient = recipient,
            Subject = subject,
            Purpose = purpose,
            ErrorReason = errorReason,
            AttemptCount = attemptCount,
            CorrelationId = correlationId,
            FailedAt = failedAt
        };

        if (id.HasValue)
        {
            entity.Id = id.Value;
        }

        return entity;
    }
}
