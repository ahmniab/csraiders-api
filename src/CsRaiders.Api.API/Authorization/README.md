# Authorization

## Overview
Provides dynamic, policy-based, and attribute-driven permission evaluation and authorization policies for the CsRaiders API. Integrates in-memory caching and invalidation tokens to resolve database-backed user and role permissions without baking permissions into JWT tokens.

## Interface
- `AuthPolicies` static class with `AuthenticatedOnly` policy constant and `AddAuthPolicies` extension method (`[AuthPolicies.cs:AuthPolicies]`, `[AuthPolicies.cs:AuthPolicies.AuthenticatedOnly]`, `[AuthPolicies.cs:AuthPolicies.AddAuthPolicies]`)
- `HasPermissionAttribute` attribute for declaring endpoint permission requirements (`[HasPermissionAttribute.cs:HasPermissionAttribute]`)
- `PermissionAuthorizationPolicyProvider` dynamic policy provider resolving `permission:` prefix policies (`[PermissionAuthorizationPolicyProvider.cs:PermissionAuthorizationPolicyProvider]`, `[PermissionAuthorizationPolicyProvider.cs:PermissionAuthorizationPolicyProvider.GetPolicyAsync]`)
- `PermissionRequirement` authorization requirement record specifying the target permission string (`[PermissionAuthorizationHandler.cs:PermissionRequirement]`)
- `PermissionAuthorizationHandler` authorization handler evaluating permissions against cached database roles and user overrides (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler]`, `[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`)

## Invariants
- `AuthenticatedOnly` policy requires both an authenticated user and a valid `sid` claim (`[AuthPolicies.cs:AuthPolicies.AddAuthPolicies]`).
- `HasPermissionAttribute` prefixes all permission strings with `permission:` (`[HasPermissionAttribute.cs:HasPermissionAttribute.PolicyPrefix]`).
- Dynamic permission policy evaluation only applies to policy names prefixed with `permission:`; other policy names fall back to default policy provider (`[PermissionAuthorizationPolicyProvider.cs:PermissionAuthorizationPolicyProvider.GetPolicyAsync]`).
- Requests must contain a parseable GUID `sub` claim in user claims, or permission handling halts without succeeding (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`).
- Users missing from the repository or with `CanAuthenticate == false` are granted an empty set of permissions (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`).
- Cached permissions expire after 30 seconds or upon invalidation via `IRbacInvalidationToken.CurrentToken` (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`).

## Side Effects
- Queries `IUserRepository.FindByIdWithPermissionsAsync` and `IRoleRepository.FindByIdsAsync` on cache misses (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`).
- Reads from and writes effective permission hash sets to `IMemoryCache` (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`).
- Increments `permission_cache.hit` and `permission_cache.miss` metrics via `IAuthMetricsRecorder.Increment` (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`).

## Verification
- Run: `dotnet test`
