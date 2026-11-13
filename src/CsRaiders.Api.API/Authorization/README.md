# Authorization

## Overview
Provides dynamic, policy-based, and attribute-driven permission evaluation and authorization policies for the CsRaiders API. Evaluates user permissions entirely in-memory using 64-bit integer bitmasks (`perms` claim) embedded in access tokens, eliminating database queries and cache lookups during request authorization.

## Interface
- `AuthPolicies` static class with `AuthenticatedOnly` policy constant and `AddAuthPolicies` extension method (`[AuthPolicies.cs:AuthPolicies]`, `[AuthPolicies.cs:AuthPolicies.AuthenticatedOnly]`, `[AuthPolicies.cs:AuthPolicies.AddAuthPolicies]`)
- `HasPermissionAttribute` attribute for declaring endpoint permission requirements evaluated in-memory from access token bitmask claims (`[HasPermissionAttribute.cs:HasPermissionAttribute]`)
- `PermissionAuthorizationPolicyProvider` dynamic policy provider resolving `permission:` prefix policies (`[PermissionAuthorizationPolicyProvider.cs:PermissionAuthorizationPolicyProvider]`, `[PermissionAuthorizationPolicyProvider.cs:PermissionAuthorizationPolicyProvider.GetPolicyAsync]`)
- `PermissionRequirement` authorization requirement record specifying the target permission string (`[PermissionAuthorizationHandler.cs:PermissionRequirement]`)
- `PermissionAuthorizationHandler` pure in-memory authorization handler evaluating required permission bits against the access token's `perms` claim using bitwise AND operations (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler]`, `[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`)

## Invariants
- `AuthenticatedOnly` policy requires both an authenticated user and a valid `sid` claim (`[AuthPolicies.cs:AuthPolicies.AddAuthPolicies]`).
- `HasPermissionAttribute` prefixes all permission strings with `permission:` (`[HasPermissionAttribute.cs:HasPermissionAttribute.PolicyPrefix]`).
- Dynamic permission policy evaluation only applies to policy names prefixed with `permission:`; other policy names fall back to default policy provider (`[PermissionAuthorizationPolicyProvider.cs:PermissionAuthorizationPolicyProvider.GetPolicyAsync]`).
- Access tokens must contain a valid, parseable unsigned 64-bit integer `perms` claim; missing, malformed, or negative claims result in immediate authorization failure (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`).
- Unknown or unregistered permissions map to `0UL`, failing closed without database or cache fallback (`[PermissionAuthorizationHandler.cs:PermissionAuthorizationHandler.HandleRequirementAsync]`).
- Permission authorization checks are stateless, CPU-bound, and strictly execute in-memory via `(bitmask & requiredBit) != 0UL`.

## Side Effects
- None (zero database queries, zero cache lookups, and zero metrics mutations during authorization evaluation).

## Verification
- Run: `dotnet test`
