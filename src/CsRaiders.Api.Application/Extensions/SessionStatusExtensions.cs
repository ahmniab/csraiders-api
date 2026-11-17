using CsRaiders.Api.Application.Extensions;

using CsRaiders.Api.Application.Common.Interfaces.Security;

namespace CsRaiders.Api.Application.Extensions;

public static class SessionStatusExtensions
{
    public static bool IsActive(this SessionStatus session, DateTimeOffset now)
        => SessionExtensions.IsActiveCore(session.RevokedAt, session.ExpiresAt, session.IdleExpiresAt, now);
}