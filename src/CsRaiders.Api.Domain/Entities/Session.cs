using System.Net;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Domain.Entities;

public sealed class Session : Entity
{
    public Session() { }

    /// <summary>Session identity is decided up-front: the refresh token embeds it (jti/sid).</summary>
    public Session(Guid id) : base(id) { }

    public Guid UserId { get; set; }
    public byte[] RefreshTokenHash { get; set; } = [];
    public byte[]? PreviousRefreshTokenHash { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public string DeviceFp { get; set; } = null!;
    public IPAddress? Ip { get; set; }
    public string? UserAgent { get; set; }
    public bool IsStudentSession { get; set; }
    public bool EnforceSingleSession { get; set; }   // snapshot of !has(session:multi) at issue time (FR-61)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset IdleExpiresAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public SessionRevokeReason? RevokeReason { get; set; }
}
