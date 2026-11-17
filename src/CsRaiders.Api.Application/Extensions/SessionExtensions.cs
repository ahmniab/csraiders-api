using CsRaiders.Api.Application.Common.Interfaces.Security;

namespace CsRaiders.Api.Application.Extensions;

public static class SessionExtensions
{
    public static bool IsActive(this SessionStatus session)
    {
        return session.ExpiresAt <= DateTimeOffset.Now 
               && session.IdleExpiresAt <= DateTimeOffset.Now
               && session.RevokedAt == null;
    }
}