# Constants

## Overview
Centralized domain constants, system permission strings, authentication timing rules, lockout thresholds, and predefined role configurations.

## Interface
- [AuthRules.cs:AuthRules]: Authentication policy constants covering token lifetimes, OTP thresholds, brute-force lockout rules, and concurrency session caps.
- [PermissionCatalog.cs:PermissionCatalog]: Granular permission string keys (`course:read`, `session:multi`, etc.), complete catalogue collections (`All`, `StudentAllowed`), and lookup helper `IsKnown(string)`.
- [RolePresets.cs:RolePreset]: Immutable record defining default role blueprints (`Name`, `Slug`, `Scope`, `Permissions`).
- [RolePresets.cs:RolePresets]: Built-in role definitions (`Student`, `Support`, `ContentManager`, `Administrator`, `SuperAdmin`) and aggregation array `All`.

## Invariants
- [PermissionCatalog.cs:PermissionCatalog.StudentAllowed]: Student roles are constrained strictly to `CourseRead` and `SessionMulti`.
- [RolePresets.cs:RolePresets.Administrator]: Administrator preset explicitly excludes `RoleManage` and `CoursePurge` (reserved strictly for `SuperAdmin`).
- [PermissionCatalog.cs:PermissionCatalog.IsKnown]: Validates permissions using ordinal case-sensitive comparison against known catalog entries.

## Side Effects
- None

## Verification
- Run: `dotnet test`
