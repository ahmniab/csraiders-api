using System.Net;
using System.Security.Cryptography;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Domain.Entities;

public sealed class Session : Entity
{
    private Session() { }

    public Guid UserId { get; private set; }
    public byte[] RefreshTokenHash { get; private set; } = [];
    public byte[]? PreviousRefreshTokenHash { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public string DeviceFp { get; private set; } = null!;
    public IPAddress? Ip { get; private set; }
    public string? UserAgent { get; private set; }
    public bool IsStudentSession { get; private set; }
    public bool EnforceSingleSession { get; private set; }   // snapshot of !has(session:multi) at issue time (FR-61)
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset IdleExpiresAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public SessionRevokeReason? RevokeReason { get; private set; }

    public static Session Open(Guid userId, byte[] refreshTokenHash, string deviceFp, IPAddress? ip, string? userAgent,
                               bool isStudentSession, bool enforceSingleSession, DateTimeOffset now)
    {
        if (refreshTokenHash.Length != 32) throw new ArgumentException("Expected a SHA-256 hash.", nameof(refreshTokenHash));
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceFp);

        return new Session
        {
            UserId = userId,
            RefreshTokenHash = refreshTokenHash,
            DeviceFp = deviceFp,
            Ip = ip,
            UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent,
            IsStudentSession = isStudentSession,
            EnforceSingleSession = enforceSingleSession,
            CreatedAt = now,
            LastSeenAt = now,
            IdleExpiresAt = now + AuthRules.RefreshIdleLifetime,
            ExpiresAt = now + AuthRules.RefreshAbsoluteLifetime
        };
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt && now < IdleExpiresAt;

    /// <summary>
    /// Rotation + reuse detection (FR-63). Replaying an already-rotated token revokes the whole session.
    /// The caller MUST commit even when the outcome is ReuseDetected, because the revocation is the point.
    /// </summary>
    public RefreshOutcome TryRotate(byte[] presentedHash, byte[] newHash, string deviceFp, DateTimeOffset now)
    {
        if (RevokedAt is not null) return RefreshOutcome.Inactive;

        if (PreviousRefreshTokenHash is not null && Same(PreviousRefreshTokenHash, presentedHash))
        {
            Revoke(SessionRevokeReason.ReuseDetected, now);
            return RefreshOutcome.ReuseDetected;
        }

        if (!Same(RefreshTokenHash, presentedHash)) return RefreshOutcome.InvalidToken;
        if (now >= ExpiresAt || now >= IdleExpiresAt) return RefreshOutcome.Inactive;
        if (!string.Equals(DeviceFp, deviceFp, StringComparison.Ordinal)) return RefreshOutcome.DeviceMismatch;

        PreviousRefreshTokenHash = RefreshTokenHash;
        RefreshTokenHash = newHash;
        RotatedAt = now;
        LastSeenAt = now;
        IdleExpiresAt = now + AuthRules.RefreshIdleLifetime;
        return RefreshOutcome.Rotated;
    }

    /// <summary>Throttled last_seen_at write (T3.6). Returns true when the row changed and needs saving.</summary>
    public bool TouchIfDue(DateTimeOffset now)
    {
        if (now - LastSeenAt < AuthRules.SessionTouchThrottle) return false;
        LastSeenAt = now;
        IdleExpiresAt = now + AuthRules.RefreshIdleLifetime;
        return true;
    }

    /// <summary>Idempotent: the first reason wins.</summary>
    public void Revoke(SessionRevokeReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokeReason = reason;
    }

    private static bool Same(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);
}
