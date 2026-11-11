using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Infrastructure.Services;

/// <summary>
/// Serializes policy reduction with login issuance using the same advisory lock.
/// Older sessions are revoked before the survivor is marked single-session,
/// preserving the partial unique index invariant.
/// </summary>
public sealed class SessionReconciliationService(
    IUserRepository users,
    ISessionRepository sessions,
    ISessionCacheHook cache,
    IPostCommitActions postCommit,
    IPlatformClock clock) : ISessionReconciliationService
{
    public async Task ReconcileAfterMultiSessionLossAsync(Guid userId, CancellationToken ct = default)
    {
        await users.AcquireLoginLockAsync(userId, ct);
        var now = clock.UtcNow;
        var active = await sessions.FindActiveByUserIdAsync(userId, now, ct: ct);
        if (active.Count == 0) return;

        var survivor = active.OrderByDescending(x => x.CreatedAt).First();
        await sessions.BulkRevokeByUserIdAsync(userId, SessionRevokeReason.PolicyReduced, survivor.Id, now, ct);
        foreach (var session in active.Where(x => x.Id != survivor.Id))
        {
            var sessionId = session.Id;
            postCommit.Enqueue(() => cache.Evict(sessionId));
        }

        // The database revocation is applied before the sole survivor is marked,
        // so the unique single-session index can never observe two active rows.
        await sessions.SetEnforceSingleSessionAsync(userId, true, now, ct);
    }
}
