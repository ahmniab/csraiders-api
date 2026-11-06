using System.Net;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Constants;
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
    private LoginAttempt() { }

    public Email Email { get; private set; } = null!;
    public Guid? UserId { get; private set; }
    public IPAddress? LastIp { get; private set; }
    public int FailedCount { get; private set; }
    public DateTimeOffset? FirstFailureAt { get; private set; }
    public DateTimeOffset? DelayUntil { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static LoginAttempt For(Email email, DateTimeOffset now) => new() { Email = email, UpdatedAt = now };

    /// <summary>Check this BEFORE verifying the password, so a locked account never costs an Argon2id run.</summary>
    public LoginGate Evaluate(DateTimeOffset now)
    {
        if (LockedUntil is { } lockedUntil && lockedUntil > now) return LoginGate.Locked(lockedUntil);
        if (DelayUntil is { } delayUntil && delayUntil > now) return LoginGate.Throttled(delayUntil);
        return LoginGate.Open;
    }

    public LoginFailureOutcome RegisterFailure(DateTimeOffset now, IPAddress? ip, Guid? userId)
    {
        if (LockedUntil is { } stillLocked && stillLocked > now) return LoginFailureOutcome.LockedOut;

        var lockExpired = LockedUntil is not null;                       // lock existed and has elapsed
        var windowExpired = FirstFailureAt is { } first && now - first > AuthRules.LoginFailureWindow;
        if (lockExpired || windowExpired) ResetCounters();

        FirstFailureAt ??= now;
        FailedCount++;
        LastIp = ip;
        UserId ??= userId;
        UpdatedAt = now;

        if (FailedCount >= AuthRules.LoginLockoutAfterFailures)
        {
            LockedUntil = now + AuthRules.LoginLockoutDuration;
            DelayUntil = null;
            return LoginFailureOutcome.LockedOut;
        }

        if (FailedCount > AuthRules.LoginDelayAfterFailures)
        {
            var seconds = Math.Min(Math.Pow(2, FailedCount - AuthRules.LoginDelayAfterFailures),
                                   AuthRules.LoginMaxDelay.TotalSeconds);
            DelayUntil = now + TimeSpan.FromSeconds(seconds);
            return LoginFailureOutcome.Throttled;
        }

        return LoginFailureOutcome.Recorded;
    }

    /// <summary>Successful login, password reset or admin unlock (D8).</summary>
    public void Reset(DateTimeOffset now)
    {
        ResetCounters();
        UpdatedAt = now;
    }

    private void ResetCounters()
    {
        FailedCount = 0;
        FirstFailureAt = null;
        DelayUntil = null;
        LockedUntil = null;
    }
}
