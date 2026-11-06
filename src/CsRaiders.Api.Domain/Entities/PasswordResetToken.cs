using System.Net;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Constants;

namespace CsRaiders.Api.Domain.Entities;

public sealed class PasswordResetToken : Entity
{
    private PasswordResetToken() { }

    public Guid UserId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public IPAddress? RequestedIp { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static PasswordResetToken Issue(Guid userId, byte[] tokenHash, IPAddress? ip, DateTimeOffset now) => new()
    {
        UserId = userId, TokenHash = tokenHash, RequestedIp = ip, CreatedAt = now,
        ExpiresAt = now + AuthRules.PasswordResetLifetime
    };

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    /// <summary>Single use (FR-65).</summary>
    public bool TryConsume(DateTimeOffset now)
    {
        if (!IsUsable(now)) return false;
        UsedAt = now;
        return true;
    }

    /// <summary>Used when a newer reset is requested: older unused tokens die.</summary>
    public void Invalidate(DateTimeOffset now) => UsedAt ??= now;
}
