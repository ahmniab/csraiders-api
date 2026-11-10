using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Enums;
using Mediator;

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
    ISessionCacheHook cache,
    ISecurityEventRepository audit)
    : IRequestHandler<RefreshTokenCommand, RefreshTokenResult>
{
    public async ValueTask<RefreshTokenResult> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return RefreshTokenResult.Failure("invalid_grant");
        if (string.IsNullOrWhiteSpace(request.DeviceFingerprint))
            return RefreshTokenResult.Failure("session_superseded");

        byte[] presentedHash;
        try { presentedHash = tokens.HashRefreshToken(request.RefreshToken); }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return RefreshTokenResult.Failure("invalid_grant"); }

        var now = clock.UtcNow;
        var session = await sessions.FindByRefreshHashForUpdateAsync(presentedHash, ct);
        if (session is null) return RefreshTokenResult.Failure("invalid_grant");

        var nextRefresh = tokens.CreateRefreshToken();
        var outcome = session.TryRotate(presentedHash, tokens.HashRefreshToken(nextRefresh), request.DeviceFingerprint, now);
        switch (outcome)
        {
            case RefreshOutcome.Rotated:
                sessions.Update(session);
                cache.Evict(session.Id);
                return new(true, tokens.CreateAccessToken(session.UserId, session.Id, now), nextRefresh, session.Id);
            case RefreshOutcome.DeviceMismatch:
                return RefreshTokenResult.Failure("session_superseded");
            case RefreshOutcome.ReuseDetected:
                sessions.Update(session);
                await audit.AddAsync("token_reuse_detected", session.UserId, createdAt: now, ct: ct);
                cache.Evict(session.Id);
                return RefreshTokenResult.Failure("session_compromised");
            case RefreshOutcome.Inactive:
                return RefreshTokenResult.Failure("session_superseded");
            default:
                return RefreshTokenResult.Failure("invalid_grant");
        }
    }
}
