# Policies

## Overview
Domain policy implementations encapsulating authorization resolution logic and multi-session concurrency enforcement rules.

## Interface
- [EffectivePermissions.cs:EffectivePermissions.Resolve]: Calculates the effective permission set from assigned roles and user-specific grant/deny overrides.
- [SessionPolicy.cs:SessionDecision]: Record struct representing session enforcement directives (`EnforceSingleSession`, `Action`, `RevokeCount`).
- [SessionPolicy.cs:SessionPolicy.Decide]: Evaluates active session count, multi-session permission, and concurrency limit to determine session revocation actions.

## Invariants
- [EffectivePermissions.cs:EffectivePermissions.Resolve]: Explicit `Deny` overrides always take precedence over role permissions and explicit `Grant` overrides (`DENY always wins`).
- [SessionPolicy.cs:SessionPolicy.Decide]: If `hasSessionMulti` is false, `EnforceSingleSession` is true and any existing active sessions require `SessionAction.RevokeAll`.
- [SessionPolicy.cs:SessionPolicy.Decide]: When `hasSessionMulti` is true, `maxConcurrent` must be at least 1 (`ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1)`).

## Side Effects
- None

## Verification
- Run: `dotnet test`
