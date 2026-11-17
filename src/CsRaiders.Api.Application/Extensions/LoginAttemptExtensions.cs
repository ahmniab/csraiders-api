using System.Net;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Extensions;

public static class LoginAttemptExtensions
{
    /// <summary>Check this BEFORE verifying the password, so a locked account never costs an Argon2id run.</summary>
    public static LoginGate Evaluate(this LoginAttempt attempt, DateTimeOffset now)
    {
        if (attempt.LockedUntil is { } lockedUntil && lockedUntil > now) return LoginGate.Locked(lockedUntil);
        if (attempt.DelayUntil is { } delayUntil && delayUntil > now) return LoginGate.Throttled(delayUntil);
        return LoginGate.Open;
    }

    public static LoginFailureOutcome RegisterFailure(this LoginAttempt attempt, DateTimeOffset now, IPAddress? ip, Guid? userId)
    {
        if (attempt.LockedUntil is { } stillLocked && stillLocked > now) return LoginFailureOutcome.LockedOut;

        var lockExpired = attempt.LockedUntil is not null;                       // lock existed and has elapsed
        var windowExpired = attempt.FirstFailureAt is { } first && now - first > AuthRules.LoginFailureWindow;
        if (lockExpired || windowExpired) ResetCounters(attempt);

        attempt.FirstFailureAt ??= now;
        attempt.FailedCount++;
        attempt.LastIp = ip;
        attempt.UserId ??= userId;
        attempt.UpdatedAt = now;

        if (attempt.FailedCount >= AuthRules.LoginLockoutAfterFailures)
        {
            attempt.LockedUntil = now + AuthRules.LoginLockoutDuration;
            attempt.DelayUntil = null;
            return LoginFailureOutcome.LockedOut;
        }

        if (attempt.FailedCount > AuthRules.LoginDelayAfterFailures)
        {
            var seconds = Math.Min(Math.Pow(2, attempt.FailedCount - AuthRules.LoginDelayAfterFailures),
                                   AuthRules.LoginMaxDelay.TotalSeconds);
            attempt.DelayUntil = now + TimeSpan.FromSeconds(seconds);
            return LoginFailureOutcome.Throttled;
        }

        return LoginFailureOutcome.Recorded;
    }

    /// <summary>Successful login, password reset or admin unlock (D8).</summary>
    public static void Reset(this LoginAttempt attempt, DateTimeOffset now)
    {
        ResetCounters(attempt);
        attempt.UpdatedAt = now;
    }

    private static void ResetCounters(LoginAttempt attempt)
    {
        attempt.FailedCount = 0;
        attempt.FirstFailureAt = null;
        attempt.DelayUntil = null;
        attempt.LockedUntil = null;
    }
}
