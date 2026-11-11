namespace CsRaiders.Api.Application.Common.Interfaces.Security;

/// <summary>
/// A process-wide invalidation signal for effective-permission cache entries.
/// Every RBAC mutation invalidates all entries so a newly revoked permission is
/// never retained for the cache TTL.
/// </summary>
public interface IRbacInvalidationToken
{
    CancellationToken CurrentToken { get; }
    void Invalidate();
}
