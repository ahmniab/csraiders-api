using Microsoft.AspNetCore.Authorization;

namespace CsRaiders.Api.API.Authorization;

/// <summary>Declares a permission requirement evaluated in-memory from access token bitmask claims with zero database lookups.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    internal const string PolicyPrefix = "permission:";

    public HasPermissionAttribute(string permission) 
        => Policy = PolicyPrefix + permission;
}
