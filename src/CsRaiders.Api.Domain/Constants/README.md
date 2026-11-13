# Constants

## Overview
Centralized domain constants, system permission strings, 64-bit authorization bitmasks, authentication timing rules, lockout thresholds, sensitive capability definitions, and predefined role configurations.

## Interface
- `[AuthRules.cs:AuthRules]`: Authentication policy constants covering 5-minute access token lifetimes, refresh token rules, OTP thresholds, brute-force lockout rules, and concurrency session caps.
- `[PermissionBits.cs:PermissionBits]`: 64-bit domain flags enum mapping all 20 catalog permissions to explicit powers of two (`1UL << 0` to `1UL << 19`) and `None = 0UL`.
- `[PermissionBitmaskMapper.cs:PermissionBitmaskMapper]`: Static utility providing fail-closed bidirectional mapping between permission strings and 64-bit integer bitmasks (`GetBit`, `ToBitmask`, `HasPermission`, `ToPermissionNames`).
- `[PermissionCatalog.cs:PermissionCatalog]`: Granular permission string keys (`course:read`, `session:multi`, etc.), complete catalogue collections (`All`, `StudentAllowed`), and lookup helper `IsKnown(string)`.
- `[RolePresets.cs:RolePreset]`: Immutable record defining default role blueprints (`Name`, `Slug`, `Scope`, `Permissions`).
- `[RolePresets.cs:RolePresets]`: Built-in role definitions (`Student`, `Support`, `ContentManager`, `Administrator`, `SuperAdmin`) and aggregation array `All`.
- `[SensitivePermissions.cs:SensitivePermissions]`: Catalog of high-risk permissions (`role:manage`, `payment:refund`, `user:write`, `session:revoke`) and precomputed bitmask `SensitiveBits` requiring immediate session revocation upon de-escalation.

## Invariants
- `[AuthRules.cs:AuthRules.AccessTokenLifetime]`: Access token lifetime is constrained to exactly 5 minutes (`TimeSpan.FromMinutes(5)`), bounding permission propagation latency upon authorization changes.
- `[PermissionBits.cs:PermissionBits]`: Each permission bit is strictly a distinct power of two up to 2^19; bit order and values remain immutable to guarantee binary backward-compatibility.
- `[PermissionBitmaskMapper.cs:PermissionBitmaskMapper]`: Unknown, null, or empty permission strings fail closed by mapping to `0UL`; bitwise authorization checks require non-zero matching bits.
- `[SensitivePermissions.cs:SensitivePermissions]`: Any removal or loss of designated sensitive permissions must immediately trigger session revocation within 5 seconds.
- `[PermissionCatalog.cs:PermissionCatalog.StudentAllowed]`: Student roles are constrained strictly to `CourseRead` and `SessionMulti`.
- `[RolePresets.cs:RolePresets.Administrator]`: Administrator preset explicitly excludes `RoleManage` and `CoursePurge` (reserved strictly for `SuperAdmin`).
- `[PermissionCatalog.cs:PermissionCatalog.IsKnown]`: Validates permissions using ordinal case-sensitive comparison against known catalog entries.

## Side Effects
- None

## Verification
- Run: `dotnet test`
