using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using Mediator;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Features.Sessions.Commands;

public sealed record RevokeSessionCommand(Guid UserId, Guid SessionId) : IRequest<RevokeSessionResult>;
public sealed record RevokeAllOtherSessionsCommand(Guid UserId, Guid CurrentSessionId) : IRequest<RevokeAllSessionsResult>;
public sealed record LogoutCommand(Guid UserId, Guid SessionId) : IRequest<RevokeSessionResult>;
public sealed record RevokeSessionResult(bool Revoked);
public sealed record RevokeAllSessionsResult(int RevokedCount);

public sealed class RevokeSessionCommandHandler(
    ISessionRepository sessions,
    IPlatformClock clock,
    IDistributedCache cache,
    ISecurityAuditWriter audit) : IRequestHandler<RevokeSessionCommand, RevokeSessionResult>
{
    public async ValueTask<RevokeSessionResult> Handle(RevokeSessionCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var session = await sessions.FindByIdAsync(request.SessionId, track: true, ct);
        if (session is null || session.UserId != request.UserId || !session.IsActive(now))
            return new(false);

        session.Revoke(SessionRevokeReason.Logout, now);
        sessions.Update(session);
        await cache.SetObjectAsync($"sid:{session.Id}", session);
        await audit.WriteAsync("session_revoked", 
            request.UserId, details: new { reason = "logout" }, ct: ct);
        return new(true);
    }
}

public sealed class LogoutCommandHandler(
    ISessionRepository sessions,
    IPlatformClock clock,
    IDistributedCache cache,
    ISecurityAuditWriter audit) : IRequestHandler<LogoutCommand, RevokeSessionResult>
{
    public async ValueTask<RevokeSessionResult> Handle(LogoutCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var session = await sessions.FindByIdAsync(request.SessionId, track: true, ct);
        if (session is null || session.UserId != request.UserId || !session.IsActive(now))
            return new(false);

        session.Revoke(SessionRevokeReason.Logout, now);
        sessions.Update(session);
        await cache.SetObjectAsync($"sid:{session.Id}", session);
        await audit.WriteAsync("session_revoked", request.UserId, details: new { reason = "logout" }, ct: ct);
        return new(true);
    }
}

public sealed class RevokeAllOtherSessionsCommandHandler(
    ISessionRepository sessions,
    IPlatformClock clock,
    IDistributedCache cache,
    ISecurityAuditWriter audit) : IRequestHandler<RevokeAllOtherSessionsCommand, RevokeAllSessionsResult>
{
    public async ValueTask<RevokeAllSessionsResult> Handle(RevokeAllOtherSessionsCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var otherSessions = (await sessions.FindActiveByUserIdAsync(request.UserId, now, ct: ct))
            .Where(s => s.Id != request.CurrentSessionId)
            .ToArray();
        if (otherSessions.Length == 0) return new(0);

        var count = await sessions.BulkRevokeByUserIdAsync(
            request.UserId, 
            SessionRevokeReason.Logout, 
            request.CurrentSessionId, 
            now, 
            ct);
        foreach (var session in otherSessions) 
            await cache.SetObjectAsync($"sid:{session.Id}", session);
        await audit.WriteAsync("session_revoked", request.UserId,
            details: new
            {
                reason = "logout", count
            }, ct: ct);
        return new(count);
    }
}
