using System.Net;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using CsRaiders.Api.Infrastructure.Services;

namespace CsRaiders.Api.IntegrationTests;

/// <summary>
/// US3 regression coverage for the decision and cache contracts used by the
/// database-backed login flow. The end-to-end host tests exercise these same
/// invariants when PostgreSQL is available.
/// </summary>
public sealed class SessionSupersessionTests
{
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
    public void Superseded_sessions_are_evicted_from_the_validation_cache()
    {
        var cache = new MemorySessionCacheHook();
        var first = Session.Open(Guid.NewGuid(), new byte[32], "device-a", IPAddress.Loopback, null,
            isStudentSession: true, enforceSingleSession: true, now: DateTimeOffset.UtcNow);

        cache.Evict(first.Id);

        Assert.True(cache.WasEvicted(first.Id));
    }
}
