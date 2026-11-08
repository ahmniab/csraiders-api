namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public readonly record struct SessionStatus(Guid SessionId, Guid UserId, DateTimeOffset ExpiresAt, DateTimeOffset IdleExpiresAt, bool Revoked);

public interface ISessionStatusReader
{
    Task<SessionStatus?> ReadAsync(Guid sessionId, CancellationToken ct = default);
}
