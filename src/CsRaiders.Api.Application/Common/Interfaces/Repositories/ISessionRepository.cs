using CsRaiders.Api.Application.Common.Models;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface ISessionRepository
{
    Task<Session?> FindByIdAsync(Guid sessionId, bool track = false, CancellationToken ct = default);
    Task<Session?> FindByRefreshHashAsync(byte[] hash, bool track = false, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> FindActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, bool track = false, CancellationToken ct = default);
    Task<int> CountActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, CancellationToken ct = default);
    Task<IReadOnlyList<SessionSummary>> ListByUserIdAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(Session session, CancellationToken ct = default);
    void Update(Session session);

    Task<int> BulkRevokeByUserIdAsync(
        Guid userId,
        SessionRevokeReason reason,
        Guid? exceptSessionId,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<int> RevokeOldestNAsync(
        Guid userId,
        int count,
        SessionRevokeReason reason,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<int> RevokeAllByUserIdAsync(
        Guid userId,
        SessionRevokeReason reason,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task SetEnforceSingleSessionAsync(
        Guid userId,
        bool enforce,
        DateTimeOffset now,
        CancellationToken ct = default);
}
