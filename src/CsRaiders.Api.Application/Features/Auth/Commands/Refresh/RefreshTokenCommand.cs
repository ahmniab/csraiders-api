using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using Mediator;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Features.Auth.Commands.Refresh;

public sealed record RefreshTokenCommand(string? RefreshToken, string? DeviceFingerprint) : IRequest<RefreshTokenResult>;

public sealed record RefreshTokenResult(
    bool Succeeded,
    string? AccessToken = null,
    string? RefreshToken = null,
    Guid? SessionId = null,
    string? ErrorCode = null)
{
    public static RefreshTokenResult Failure(string code) => new(false, ErrorCode: code);
}

public sealed class RefreshTokenCommandHandler(
    ISessionRepository sessions,
    ITokenService tokens,
    IPlatformClock clock,
    IDistributedCache cache,
    ISecurityEventRepository audit,
    IUserRepository users,
    IRoleRepository roles,
    IMediator mediator)
    : IRequestHandler<RefreshTokenCommand, RefreshTokenResult>
{
    public async ValueTask<RefreshTokenResult> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return RefreshTokenResult.Failure("invalid_grant");
        if (string.IsNullOrWhiteSpace(request.DeviceFingerprint))
            return RefreshTokenResult.Failure("session_superseded");

        var now = clock.UtcNow;
        if (!tokens.TryValidateRefreshToken(request.RefreshToken, now, out var claims))
            return RefreshTokenResult.Failure("invalid_grant");

        var session = await sessions.FindByIdForUpdateAsync(claims.SessionId, ct);
        if (session is null) return RefreshTokenResult.Failure("invalid_grant");
        if (session.UserId != claims.UserId) return RefreshTokenResult.Failure("invalid_grant");

        var presentedHash = 
            tokens.HashRefreshToken(request.RefreshToken);

        var nextRefresh = 
            tokens.CreateRefreshToken(session.UserId, session.Id, now);
        
        var outcome = session.TryRotate(
            presentedHash, 
            tokens.HashRefreshToken(nextRefresh), 
            request.DeviceFingerprint, 
            now);
        
        switch (outcome)
        {
            case RefreshOutcome.Rotated:
                sessions.Update(session);
                await cache.RemoveAsync($"sid:{session.Id}");
                var prems = await ResolvePermissionsBitmaskAsync(session.UserId, ct);
                if (prems is null) 
                    return RefreshTokenResult.Failure("session_superseded");
                return new(
                    true, 
                    tokens.CreateAccessToken(
                        session.UserId, 
                        session.Id, 
                        prems, 
                        now), 
                    nextRefresh, 
                    session.Id);
            
            case RefreshOutcome.DeviceMismatch:
                return RefreshTokenResult.Failure("session_superseded");
            
            case RefreshOutcome.ReuseDetected:
                sessions.Update(session);
                await audit.AddAsync("token_reuse_detected", session.UserId, createdAt: now, ct: ct);
                await cache.RemoveAsync($"sid:{session.Id}");
                return RefreshTokenResult.Failure("session_compromised");
            
            case RefreshOutcome.Inactive:
                return RefreshTokenResult.Failure("session_superseded");
            
            default:
                return RefreshTokenResult.Failure("invalid_grant");
        }
    }

    private async Task<IReadOnlySet<string>?> ResolvePermissionsBitmaskAsync(Guid userId, CancellationToken ct)
    {
        var cacheKey = $"perms:{userId:D}";
        
        var perms = await cache.GetObjectAsync<IReadOnlySet<string>>(cacheKey);
        if (perms != null)
            return perms;
    
        var userWithPerms = await users.FindByIdWithPermissionsAsync(userId, ct: ct);
        if (userWithPerms is null || !userWithPerms.CanAuthenticate())
        {
            return null;
        }
    
        var assignedRoles = userWithPerms.Roles.Count > 0
            ? await roles.FindByIdsAsync(userWithPerms.Roles.Select(r => r.RoleId), ct)
            : [];
        perms 
            = EffectivePermissions.Resolve(
                assignedRoles, 
                userWithPerms.PermissionOverrides);
    
        await cache.SetObjectAsync(cacheKey,perms);
        return perms;
    }
}
