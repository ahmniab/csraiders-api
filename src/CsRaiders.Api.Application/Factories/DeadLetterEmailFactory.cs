using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="DeadLetterEmail"/>.</summary>
public static class DeadLetterEmailFactory
{
    public static DeadLetterEmail Create(
        string recipient,
        string subject,
        string purpose,
        string errorReason,
        int attemptCount,
        string? correlationId,
        DateTimeOffset failedAt) => new()
    {
        Recipient = recipient,
        Subject = subject,
        Purpose = purpose,
        ErrorReason = errorReason,
        AttemptCount = attemptCount,
        CorrelationId = correlationId,
        FailedAt = failedAt
    };
}
