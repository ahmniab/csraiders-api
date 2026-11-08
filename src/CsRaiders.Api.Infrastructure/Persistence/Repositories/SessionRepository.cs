using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Models;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence.Repositories;

public sealed class SessionRepository(AppDbContext db) : ISessionRepository, ISessionStatusReader
{
    public async Task<SessionStatus?> ReadAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.Sessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => new SessionStatus(s.Id, s.UserId, s.ExpiresAt, s.IdleExpiresAt, s.RevokedAt != null))
            .SingleOrDefaultAsync(ct);
    public async Task<Session?> FindByIdAsync(Guid sessionId, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.Sessions.AsQueryable() : db.Sessions.AsNoTracking();
        return await query.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    }

    public async Task<Session?> FindByRefreshHashAsync(byte[] hash, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.Sessions.AsQueryable() : db.Sessions.AsNoTracking();
        return await query.FirstOrDefaultAsync(s =>
            s.RefreshTokenHash == hash ||
            s.PreviousRefreshTokenHash == hash, ct);
    }

    public async Task<IReadOnlyList<Session>> FindActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.Sessions.AsQueryable() : db.Sessions.AsNoTracking();
        query = query.Where(s => s.UserId == userId && s.RevokedAt == null);

        if (now.HasValue)
        {
            var timestamp = now.Value;
            query = query.Where(s => timestamp < s.ExpiresAt && timestamp < s.IdleExpiresAt);
        }

        return await query
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<int> CountActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var query = db.Sessions.AsNoTracking().Where(s => s.UserId == userId && s.RevokedAt == null);

        if (now.HasValue)
        {
            var timestamp = now.Value;
            query = query.Where(s => timestamp < s.ExpiresAt && timestamp < s.IdleExpiresAt);
        }

        return await query.CountAsync(ct);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new SessionSummary(
                s.Id,
                s.DeviceFp,
                s.Ip,
                s.UserAgent,
                s.IsStudentSession,
                s.CreatedAt,
                s.LastSeenAt,
                s.ExpiresAt,
                s.RevokedAt,
                s.RevokeReason))
            .ToListAsync(ct);
    }

    public async Task AddAsync(Session session, CancellationToken ct = default)
    {
        await db.Sessions.AddAsync(session, ct);
    }

    public void Update(Session session)
    {
        db.Sessions.Update(session);
    }

    public async Task<int> BulkRevokeByUserIdAsync(
        Guid userId,
        SessionRevokeReason reason,
        Guid? exceptSessionId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null);

        if (exceptSessionId.HasValue)
        {
            query = query.Where(s => s.Id != exceptSessionId.Value);
        }

        return await query.ExecuteUpdateAsync(setter => setter
            .SetProperty(s => s.RevokedAt, now)
            .SetProperty(s => s.RevokeReason, reason), ct);
    }

    public async Task<int> RevokeOldestNAsync(
        Guid userId,
        int count,
        SessionRevokeReason reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (count <= 0) return 0;

        var oldestSessionIds = await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .OrderBy(s => s.CreatedAt)
            .Take(count)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (oldestSessionIds.Count == 0) return 0;

        return await db.Sessions
            .Where(s => oldestSessionIds.Contains(s.Id))
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokeReason, reason), ct);
    }

    public async Task<int> RevokeAllByUserIdAsync(
        Guid userId,
        SessionRevokeReason reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        return await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokeReason, reason), ct);
    }

    public async Task SetEnforceSingleSessionAsync(
        Guid userId,
        bool enforce,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(s => s.EnforceSingleSession, enforce), ct);
    }
}
