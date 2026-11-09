using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;

namespace CsRaiders.Api.IntegrationTests;

public sealed class LoginConcurrencyTests
{
    [Fact]
    public void Single_session_policy_reclaims_all_existing_sessions()
    {
        var decision = SessionPolicy.Decide(hasSessionMulti: false, activeSessions: 10, maxConcurrent: 3);
        Assert.True(decision.EnforceSingleSession);
        Assert.Equal(SessionAction.RevokeAll, decision.Action);
        Assert.Equal(10, decision.RevokeCount);
    }

    [Fact]
    public void Multi_session_policy_reclaims_only_overflow()
    {
        var decision = SessionPolicy.Decide(hasSessionMulti: true, activeSessions: 3, maxConcurrent: 3);
        Assert.False(decision.EnforceSingleSession);
        Assert.Equal(SessionAction.RevokeOldest, decision.Action);
        Assert.Equal(1, decision.RevokeCount);
    }
}
