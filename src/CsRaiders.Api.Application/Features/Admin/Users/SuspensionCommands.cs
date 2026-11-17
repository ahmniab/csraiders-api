using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Enums;
using Mediator;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Features.Admin.Users;

public sealed record SuspendUserCommand(Guid GrantorUserId, Guid UserId) : IRequest<SuspensionResult>;
public sealed record ReactivateUserCommand(Guid GrantorUserId, Guid UserId) : IRequest<SuspensionResult>;
public sealed record SuspensionResult(bool Succeeded, bool NotFound = false, bool InvalidState = false)
{
    public static SuspensionResult Missing() => new(false, NotFound: true);
    public static SuspensionResult Invalid() => new(false, InvalidState: true);
}

public sealed class SuspendUserCommandHandler(
    IUserRepository users,
    ISessionRepository sessions,
    IDistributedCache cache,
    IPlatformClock clock,
    ISecurityAuditWriter audit) : IRequestHandler<SuspendUserCommand, SuspensionResult>
{
    public async ValueTask<SuspensionResult> Handle(SuspendUserCommand request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(request.UserId, track: true, ct);
        if (user is null) return SuspensionResult.Missing();
        if (user.Status == UserStatus.Deleted || user.Status == UserStatus.Suspended) return SuspensionResult.Invalid();

        var now = clock.UtcNow;
        user.Suspend();
        users.Update(user);
        var active = await sessions.FindActiveByUserIdAsync(user.Id, now, ct: ct);
        await sessions.RevokeAllByUserIdAsync(user.Id, SessionRevokeReason.AccountDisabled, now, ct);
        foreach (var session in active)
        {
            var id = session.Id;
            await cache.RemoveAsync($"sid:{id}");
        }
        await audit.WriteAsync("account_suspended", user.Id, user.Email, details: new { reason = "account_disabled" }, ct: ct);
        return new(true);
    }
}

public sealed class ReactivateUserCommandHandler(
    IUserRepository users,
    IPlatformClock clock,
    ISecurityAuditWriter audit) : IRequestHandler<ReactivateUserCommand, SuspensionResult>
{
    public async ValueTask<SuspensionResult> Handle(ReactivateUserCommand request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(request.UserId, track: true, ct);
        if (user is null) return SuspensionResult.Missing();
        if (user.Status != UserStatus.Suspended) return SuspensionResult.Invalid();
        user.Reactivate();
        users.Update(user);
        await audit.WriteAsync("account_reactivated", user.Id, user.Email, details: new { at = clock.UtcNow }, ct: ct);
        return new(true);
    }
}
