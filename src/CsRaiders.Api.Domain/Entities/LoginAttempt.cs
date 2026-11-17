using System.Net;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Domain.Entities;

public readonly record struct LoginGate(LoginGateStatus Status, DateTimeOffset? Until)
{
    public static LoginGate Open => new(LoginGateStatus.Open, null);
    public static LoginGate Throttled(DateTimeOffset until) => new(LoginGateStatus.Throttled, until);
    public static LoginGate Locked(DateTimeOffset until) => new(LoginGateStatus.Locked, until);

    public TimeSpan RetryAfter(DateTimeOffset now) => Until is { } u && u > now ? u - now : TimeSpan.Zero;
}

/// <summary>Durable brute-force state (FR-64). One row per email, including unknown emails.</summary>
public sealed class LoginAttempt : Entity
{
    public Email Email { get; set; } = null!;
    public Guid? UserId { get; set; }
    public IPAddress? LastIp { get; set; }
    public int FailedCount { get; set; }
    public DateTimeOffset? FirstFailureAt { get; set; }
    public DateTimeOffset? DelayUntil { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
