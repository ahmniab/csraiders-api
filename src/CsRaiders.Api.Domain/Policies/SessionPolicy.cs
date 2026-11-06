using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Domain.Policies;

public readonly record struct SessionDecision(bool EnforceSingleSession, SessionAction Action, int RevokeCount);

public static class SessionPolicy
{
    /// <param name="hasSessionMulti">Effective permission AFTER DENY.</param>
    /// <param name="activeSessions">Live sessions before the new one is inserted.</param>
    /// <param name="maxConcurrent">session.multi_max_concurrent (only applies to multi holders).</param>
    public static SessionDecision Decide(bool hasSessionMulti, int activeSessions, int maxConcurrent)
    {
        if (!hasSessionMulti)
            return new(EnforceSingleSession: true,
                       activeSessions > 0 ? SessionAction.RevokeAll : SessionAction.None,
                       activeSessions);

        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);

        var overflow = activeSessions - maxConcurrent + 1;     // make room for the new session
        return overflow > 0
            ? new(false, SessionAction.RevokeOldest, overflow)
            : new(false, SessionAction.None, 0);
    }
}
