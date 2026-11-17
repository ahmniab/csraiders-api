using System.Security.Cryptography;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Application.Extensions;

public static class SessionExtensions
{
    /// <summary>Single source of truth shared with <see cref="SessionStatusExtensions"/>.</summary>
    internal static bool IsActiveCore(DateTimeOffset? revokedAt, DateTimeOffset expiresAt, DateTimeOffset idleExpiresAt, DateTimeOffset now)
        => revokedAt is null && now < expiresAt && now < idleExpiresAt;

    public static bool IsActive(this Session session, DateTimeOffset now)
        => IsActiveCore(session.RevokedAt, session.ExpiresAt, session.IdleExpiresAt, now);

    /// <summary>
    /// Rotation + reuse detection (FR-63). Replaying an already-rotated token revokes the whole session.
    /// The caller MUST commit even when the outcome is ReuseDetected, because the revocation is the point.
    /// </summary>
    public static RefreshOutcome TryRotate(
        this Session s,
        byte[] presentedHash, 
        byte[] newHash, 
        string deviceFp, 
        DateTimeOffset now)
    {
        if (s.RevokedAt is not null) 
            return RefreshOutcome.Inactive;
        
        if (now >= s.ExpiresAt || now >= s.IdleExpiresAt) 
            return RefreshOutcome.Inactive;
        
        if (!string.Equals(s.DeviceFp, deviceFp, StringComparison.Ordinal)) 
            return RefreshOutcome.DeviceMismatch;

        if (s.PreviousRefreshTokenHash is not null && Same(s.PreviousRefreshTokenHash, presentedHash))
        {
            if (s.RotatedAt is { } rotatedAt && now - rotatedAt <= AuthRules.RefreshReplayGraceWindow)
            {
                return s.Rotate(newHash, now);
            }

            s.Revoke(SessionRevokeReason.ReuseDetected, now);
            return RefreshOutcome.ReuseDetected;
        }

        if (!Same(s.RefreshTokenHash, presentedHash)) return RefreshOutcome.InvalidToken;

        return s.Rotate(newHash, now);
    }

    private static RefreshOutcome Rotate(this Session s, byte[] newHash, DateTimeOffset now)
    {
        s.PreviousRefreshTokenHash = s.RefreshTokenHash;
        s.RefreshTokenHash = newHash;
        s.RotatedAt = now;
        s.LastSeenAt = now;
        s.IdleExpiresAt = now + AuthRules.RefreshIdleLifetime;
        return RefreshOutcome.Rotated;
    }

    /// <summary>Throttled last_seen_at write (T3.6). Returns true when the row changed and needs saving.</summary>
    public static bool TouchIfDue(this Session s, DateTimeOffset now)
    {
        if (now - s.LastSeenAt < AuthRules.SessionTouchThrottle) return false;
        s.LastSeenAt = now;
        s.IdleExpiresAt = now + AuthRules.RefreshIdleLifetime;
        return true;
    }

    /// <summary>Idempotent: the first reason wins.</summary>
    public static void Revoke(this Session s, SessionRevokeReason reason, DateTimeOffset now)
    {
        if (s.RevokedAt is not null) return;
        s.RevokedAt = now;
        s.RevokeReason = reason;
    }

    private static bool Same(byte[] a, byte[] b) 
        => CryptographicOperations.FixedTimeEquals(a, b);

}
