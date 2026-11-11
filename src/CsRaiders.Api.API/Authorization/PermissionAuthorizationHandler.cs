using System.Security.Claims;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Policies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace CsRaiders.Api.API.Authorization;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Resolves database-backed permissions with a globally invalidatable cache.</summary>
public sealed class PermissionAuthorizationHandler(
    IUserRepository users,
    IRoleRepository roles,
    IMemoryCache cache,
    IRbacInvalidationToken invalidation,
    IAuthMetricsRecorder metrics) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var userId)) return;

        var key = $"perms:{userId}";
        if (cache.TryGetValue(key, out HashSet<string>? cachedPermissions)) metrics.Increment("permission_cache.hit");
        else metrics.Increment("permission_cache.miss");
        var permissions = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            entry.AddExpirationToken(new CancellationChangeToken(invalidation.CurrentToken));
            var user = await users.FindByIdWithPermissionsAsync(userId);
            if (user is null || !user.CanAuthenticate) return new HashSet<string>(StringComparer.Ordinal);
            var assignedRoles = await roles.FindByIdsAsync(user.Roles.Select(x => x.RoleId));
            return new HashSet<string>(EffectivePermissions.Resolve(assignedRoles, user.PermissionOverrides), StringComparer.Ordinal);
        });

        if (permissions is not null && permissions.Contains(requirement.Permission)) context.Succeed(requirement);
    }
}
