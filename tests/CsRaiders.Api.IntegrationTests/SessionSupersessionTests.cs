using System.Net;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Models;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Application.Features.Admin;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.IntegrationTests;

/// <summary>
/// US3 regression coverage for the decision and cache contracts used by the
/// database-backed login flow. The end-to-end host tests exercise these same
/// invariants when PostgreSQL is available.
/// </summary>
public sealed class SessionSupersessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Single_session_login_supersedes_every_active_session()
    {
        var decision = SessionPolicy.Decide(hasSessionMulti: false, activeSessions: 1, maxConcurrent: 3);

        Assert.True(decision.EnforceSingleSession);
        Assert.Equal(SessionAction.RevokeAll, decision.Action);
        Assert.Equal(1, decision.RevokeCount);
    }

    [Fact]
    public void Multi_session_login_revokes_only_the_oldest_overflow()
    {
        var decision = SessionPolicy.Decide(hasSessionMulti: true, activeSessions: 3, maxConcurrent: 3);

        Assert.False(decision.EnforceSingleSession);
        Assert.Equal(SessionAction.RevokeOldest, decision.Action);
        Assert.Equal(1, decision.RevokeCount);
    }

    [Fact]
    public void Multi_session_cap_keeps_exactly_cap_active_after_rotation()
    {
        const int cap = 3;
        var active = 4;
        var decision = SessionPolicy.Decide(hasSessionMulti: true, activeSessions: active, maxConcurrent: cap);

        Assert.Equal(cap, active - decision.RevokeCount + 1);
        Assert.Equal(SessionAction.RevokeOldest, decision.Action);
    }

    [Fact]
    public async Task Superseded_sessions_are_revoked_and_evicted_from_the_validation_cache()
    {
        var userId = Guid.NewGuid();
        var session = SessionFactory.Open(userId, new byte[32], "device-a", IPAddress.Loopback, null,
            isStudentSession: true, enforceSingleSession: true, now: Now);

        var sessions = new RecordingSessionRepository(session);
        var cache = new RecordingDistributedCache();

        await RbacCommandSupport.RevokeUserSessionsAsync(userId, sessions, cache, Now, CancellationToken.None);

        Assert.Contains($"sid:{session.Id}", cache.RemovedKeys);
        Assert.Equal(1, sessions.RevokeAllCalls);
        Assert.Equal(SessionRevokeReason.AdminRevoked, sessions.LastRevokeReason);
        Assert.Equal(userId, sessions.LastRevokeUserId);
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        public List<string> RemovedKeys { get; } = [];

        public byte[]? Get(string key) => null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            Task.CompletedTask;

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => RemovedKeys.Add(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            RemovedKeys.Add(key);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSessionRepository(params Session[] sessions) : ISessionRepository
    {
        public int RevokeAllCalls { get; private set; }
        public Guid? LastRevokeUserId { get; private set; }
        public SessionRevokeReason? LastRevokeReason { get; private set; }

        public Task<Session?> FindByIdAsync(Guid sessionId, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(sessions.SingleOrDefault(s => s.Id == sessionId));
        public Task<Session?> FindByRefreshHashAsync(byte[] hash, bool track = false, CancellationToken ct = default) =>
            Task.FromResult<Session?>(null);
        public Task<Session?> FindByRefreshHashForUpdateAsync(byte[] hash, CancellationToken ct = default) =>
            Task.FromResult<Session?>(null);
        public Task<Session?> FindByIdForUpdateAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(sessions.SingleOrDefault(s => s.Id == sessionId));
        public Task<IReadOnlyList<Session>> FindActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, bool track = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Session>>(sessions.Where(s => s.UserId == userId).ToArray());
        public Task<int> CountActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, CancellationToken ct = default) =>
            Task.FromResult(sessions.Count(s => s.UserId == userId));
        public Task<IReadOnlyList<SessionSummary>> ListByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionSummary>>([]);
        public Task AddAsync(Session session, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(Session session) { }
        public Task<int> BulkRevokeByUserIdAsync(Guid userId, SessionRevokeReason reason, Guid? exceptSessionId, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task<int> RevokeOldestNAsync(Guid userId, int count, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task<int> RevokeAllByUserIdAsync(Guid userId, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default)
        {
            RevokeAllCalls++;
            LastRevokeUserId = userId;
            LastRevokeReason = reason;
            return Task.FromResult(sessions.Count(s => s.UserId == userId));
        }
        public Task SetEnforceSingleSessionAsync(Guid userId, bool enforce, DateTimeOffset now, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset now, DateTimeOffset cutoff, CancellationToken ct = default) =>
            Task.FromResult(0);
    }
}
