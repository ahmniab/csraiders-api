using System.Net;
using CsRaiders.Api.Domain.Common;

namespace CsRaiders.Api.Domain.Entities;

public sealed class PasswordResetToken : Entity
{
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public IPAddress? RequestedIp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
