using System.Globalization;
using System.Security.Claims;
using CsRaiders.Api.Domain.Constants;
using Microsoft.AspNetCore.Authorization;

namespace CsRaiders.Api.API.Authorization;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Evaluates permission requirements in-memory from access token bitmask claims.</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var permsClaim = context.User.FindFirst("perms");
        if (permsClaim == null || !ulong.TryParse(permsClaim.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bitmask))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var requiredBit = PermissionBitmaskMapper.GetBit(requirement.Permission);
        if (requiredBit == 0UL)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        if ((bitmask & requiredBit) != 0UL)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
