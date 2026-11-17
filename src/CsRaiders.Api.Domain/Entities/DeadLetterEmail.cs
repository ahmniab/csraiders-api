using CsRaiders.Api.Domain.Common;

namespace CsRaiders.Api.Domain.Entities;

public sealed class DeadLetterEmail : Entity
{
    public string Recipient { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public string ErrorReason { get; set; } = null!;
    public int AttemptCount { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset FailedAt { get; set; }
}
