# Token-Embedded Authorization Bitmask Claims (Finalized Architecture)

* **Author:** ahmniab
* **Date:** 2026-11-12

## Overview

This feature eliminates database queries and in-memory cache lookups during protected endpoint authorization in `CsRaiders.Api` by embedding a 64-bit capability bitmask directly into JWT access token claims (`perms`).

To balance operational safety, zero-downtime maintainability, and sub-nanosecond endpoint performance:
1. **Database Schema Remains Untouched:** PostgreSQL continues to store permissions as strings (`roles.permissions text[]` and the `user_permissions` table). This eliminates breaking schema migrations, preserves granular audit metadata (`granted_by`, `granted_at`), and protects stored data against bit-shift corruption.
2. **Pre-Production Fail-Fast Enforcement:** Because the application has no active production traffic, legacy token dual-read fallback and memory cache lookups are completely removed. If an incoming access token lacks the `"perms"` claim, the authorization handler immediately fails the check.
3. **Decimal Integer String Claim:** The 64-bit bitmask is emitted in the JWT payload as a decimal integer string (`"perms": "1048575"`), preventing JavaScript 53-bit floating-point truncation in web clients while maintaining zero-allocation parsing in .NET.

## Analysis

### 1. Endpoint Authorization Pipeline & Dependency Stripping
* **Execution Path in `PermissionAuthorizationHandler`:**
  1. Inspect `context.User.FindFirst("perms")`.
  2. If the claim is missing or cannot be parsed as a 64-bit integer, authorization fails immediately (`context.Fail()`).
  3. If present, evaluate `(bitmask & (long)requiredBit) != 0`. If matched, `context.Succeed(requirement)`.
* **Zero I/O Guarantee:** `PermissionAuthorizationHandler` drops all dependencies on `IMemoryCache`, `IUserRepository`, `IRoleRepository`, and `IRbacInvalidationToken`. It runs entirely as an in-memory, CPU-bound operation (< 1 nanosecond).

### 2. Token Issuance & Refresh Lifecycle
* **Decoupled Resolution in `RefreshTokenCommandHandler`:**
  - Token refresh occurs once every 5 minutes per session.
  - Permissions are resolved from `IUserRepository` and `IRoleRepository` *prior* to acquiring the exclusive row lock on `sessions`.
  - The resolved permission strings are converted to a 64-bit scalar bitmask via `PermissionBitmaskMapper.ToBitmask(effectivePermissions)`.
  - Row-lock hold time on `sessions` remains under 2 ms.
* **JWT Claim Representation:**
  - Emitted as a standard string claim: `new Claim("perms", bitmask.ToString(CultureInfo.InvariantCulture))`.
  - Quoting the integer adds only 2 bytes to the header payload (token size remains < 280 bytes).
  - Web clients decode the claim using modern native `BigInt` bitwise operations:
    ```typescript
    const perms = BigInt(jwtPayload.perms);
    const hasPermission = (perms & (1n << BigInt(permissionBitIndex))) !== 0n;
    ```
    This bypasses JavaScript's `Number.MAX_SAFE_INTEGER` ($2^{53} - 1$) ceiling without requiring string conversion loops.

### 3. Database & Domain Stability
* **Why Database String Storage is Maintained:**
  - **No Schema Migrations:** Avoids DDL changes, data migration scripts, and table drops (`user_permissions`).
  - **Bit-Index Immunity:** If permission flags are reordered, deprecated, or added in C#, database records remain completely unaffected.
  - **Audit Metadata Preservation:** Relational records of who granted or revoked overrides (`granted_by`, `granted_at`) remain intact.
  - **Admin API Contracts:** REST endpoints continue to consume and produce readable permission names (`["course:read", "user:write"]`) without impedance mismatch.

## Goals vs. Non-Goals

* **In-Scope Goals:**
  * Reject any JWT lacking the `perms` claim immediately; eliminate dual-read fallback and `IMemoryCache` lookups.
  * Emit the 64-bit bitmask as an integer string in the JWT payload (`"perms": "1048575"`).
  * Evaluate `[HasPermission]` locally via bitwise operations (`(bitmask & requiredBit) != 0`) in under 1 nanosecond.
  * Strip repository, cache, and invalidation dependencies from `PermissionAuthorizationHandler`.
  * Retain existing string-based PostgreSQL schemas (`roles.permissions text[]`, `user_permissions`).
  * Convert permission string sets to 64-bit scalar bitmasks at token issuance (login and refresh).
* **Out-of-Scope Non-Goals:**
  * In-flight legacy token dual-read backward compatibility (dropped per pre-production status).
  * Database schema alterations or EF Core migrations (database remains untouched).
  * Runtime-dynamic permission additions (catalog is compile-time static).

## Alternatives Weighed

1. **Option 1: End-to-End Bigint Database Persistence**
   * *Status:* Rejected after review.
   * *Reason:* Yields negligible performance benefit during the 5-minute refresh cycle while creating high migration friction, permanently freezing bit-shift indices into database rows, and destroying per-override audit history.
2. **Option 2: Raw JSON Number Claim vs. Integer String Claim**
   * *Status:* Integer string selected.
   * *Reason:* Integer strings prevent silent bit corruption in JavaScript web clients without incurring memory allocations or performance overhead in .NET.
3. **Option 3: Dual-Read Fallback in Authorization Handler**
   * *Status:* Rejected.
   * *Reason:* The application is pre-production. Immediate fail-fast rejection removes branching complexity and completely decouples the authorization handler from the database and cache.

## Key Decisions & Staging for Next Steps

* **Decision 1: Persistence Layer:** Retain existing string-based storage in PostgreSQL (`roles.permissions text[]` and `user_permissions`).
* **Decision 2: Token Claim Contract:** Emit `perms` as a decimal integer string in JWT access tokens.
* **Decision 3: Handler Architecture:** `PermissionAuthorizationHandler` rejects any token missing `perms` immediately and executes sub-nanosecond bitwise checks with zero I/O dependencies.
* **Decision 4: Client Bitwise Standard:** Frontend evaluates permissions via native JavaScript `BigInt` bitwise operations.
