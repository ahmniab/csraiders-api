using CsRaiders.Api.Domain.Constants;
using Microsoft.AspNetCore.Authorization;

namespace CsRaiders.Api.API.Authorization;

public static class AuthPolicies
{
    public const string AuthenticatedOnly = "AuthenticatedOnly";

    public static void AddAuthPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(AuthenticatedOnly, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim("sid"));
        foreach (var permission in PermissionCatalog.All)
        {
            options.AddPolicy(permission, p 
                => p.RequireClaim("permission", permission));
        }
    }
}
