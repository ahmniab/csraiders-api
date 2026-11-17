using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public sealed class SessionStatus()
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset IdleExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt  { get; set; }

    public static SessionStatus FromSession(Session s)
    {
        return new SessionStatus
        {
            SessionId = s.Id,
            UserId = s.UserId,
            ExpiresAt = s.ExpiresAt,
            IdleExpiresAt = s.IdleExpiresAt,
            RevokedAt = s.RevokedAt,
        };
    }
};

public interface ISessionStatusReader
{
    Task<SessionStatus?> ReadAsync(Guid sessionId, CancellationToken ct = default);
}
