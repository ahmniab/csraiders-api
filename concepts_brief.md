# Token-Embedded Authorization Bitmask Claims (Option C)

* **Author:** ahmniab
* **Date:** 2026-11-11

## Overview

This proposal replaces endpoint database and cache queries during HTTP authorization checks with a compact 32-bit capability bitmask embedded directly into JWT access token claims. Instead of serializing 20+ arbitrary permission strings into tokens (which inflates headers and triggers proxy rejections), each permission in `PermissionCatalog` maps to an individual bit in a 32-bit integer scalar (`perms`). 

Authorization checks in `PermissionAuthorizationHandler` evaluate permissions locally via a bitwise AND operation (`(userBitmask & requiredPermissionBit) != 0`), executing in sub-nanoseconds without hitting PostgreSQL or local `IMemoryCache`.

To bound authorization revocation delay to 5 minutes, access token lifetime is set to 5 minutes. The proposal includes concrete architectural hardening to mitigate database row-lock contention, office NAT rate limiting, multi-tab race conditions, and migration compatibility.

## Analysis

A multi-lens Goldfish review across system architecture, codebase contracts, operational readiness, developer ergonomics, and inventive contradiction elimination identified the following technical requirements and safeguards:

### 1. Capability Bitmask Representation (Payload Optimization)
* **Contract:** `PermissionCatalog.cs` defines 20 granular permissions. 
* **Bitmask Mapping:** In `CsRaiders.Api.Domain`, define a flags enum or static bitmask dictionary mapping each permission to a single bit `(1 << 0)` through `(1 << 19)`. 32 bits provide headroom for up to 32 permissions (or 64 bits with `ulong` for up to 64 permissions).
* **Token Payload Impact:** A single claim (`perms: 0x000F` or integer string) is embedded into the JWT by `ITokenService.CreateAccessToken(Guid userId, Guid sessionId, uint permissionsBitmask, DateTimeOffset now)`.
* **Header Size:** Total JWT size remains lean (~280 bytes vs. 1.5+ KB for raw strings), avoiding Cloudflare/ALB/Nginx `431 Request Header Fields Too Large` proxy rejections.

### 2. Authorization Handler Evaluation & In-Flight Migration Fallback
* In `PermissionAuthorizationHandler.cs`:
  1. Inspect `context.User.FindFirst("perms")`.
  2. **If claim is present:** Parse the integer scalar and evaluate `(bitmask & requiredBit) != 0`. If true, mark requirement as succeeded immediately. This bypasses `IMemoryCache` and eliminates database reads entirely.
  3. **If claim is missing (Migration Fallback):** For in-flight tokens issued prior to deployment (legacy tokens containing only `sub` and `sid`), fall back to the existing `IMemoryCache` and repository lookup. This prevents `403 Forbidden` authorization failures during zero-downtime rolling deployments.

### 3. Database Write Contention & Lock Duration Mitigation
* **Problem:** In `RefreshTokenCommandHandler.cs`, token refresh runs inside an exclusive row lock (`SELECT ... FOR UPDATE` on `sessions`). Shortening token lifetime to 5 minutes triples refresh frequency. Executing EF Core split queries (`UserRepository.FindByIdWithPermissionsAsync` and `RoleRepository.FindByIdsAsync`) while holding this row lock would tie up PostgreSQL connections and trigger write contention.
* **Mitigation:**
  - Execute permission resolution *before* acquiring the row lock on `sessions`, or utilize a short-lived cached read of user permissions.
  - The row lock transaction on `sessions` should only perform cryptographic hash verification, rotation, and commit, keeping lock hold time under 2 ms.

### 4. IP Rate Limiter Hardening for NAT Networks
* **Problem:** In `CsRaidersApiServiceExtensions.cs`, all routes under `/api/v1/auth` are throttled to **20 requests per minute per IP address**. With 5-minute tokens, multiple active users sharing an office, school campus, or mobile NAT IP will exceed 20 refresh requests per minute, triggering cascading HTTP 429 lockouts.
* **Mitigation:**
  - Partition the rate limiter: exclude `/api/v1/auth/refresh` from the blanket 20 req/min IP window, or apply a session-based / user-based rate limit for refresh endpoints while keeping strict IP limits on `/api/v1/auth/login`.

### 5. Multi-Tab Refresh Synchronization & Grace Window
* **Problem:** `Session.TryRotate` has a single `PreviousRefreshTokenHash` slot and a 15-second grace window (`AuthRules.RefreshReplayGraceWindow`). When access tokens expire every 5 minutes, single-page applications with multiple tabs open will issue concurrent refresh requests. If network lag or background tab throttling delays a request past 15 seconds, `RefreshOutcome.ReuseDetected` revokes the user's entire session (`session_compromised`).
* **Mitigation:**
  - Frontend client must implement a singleton refresh promise/mutex so concurrent 401s reuse a single in-flight refresh call.
  - SRE observability: Record OpenTelemetry metrics distinguishing within-session rotation races from cross-client token reuse attacks.

### 6. Revocation Lag Acceptance & Boundary
* **Accepted Trade-off:** Administrative role demotions and permission revocations will take up to 5 minutes (+ 30 seconds clock skew tolerance) to take effect on active access tokens. 
* Immediate account suspension or forced logout continues to be enforced on every request by `SessionValidationMiddleware.cs` via session revocation checks.

## Goals vs. Non-Goals

* **In-Scope Goals:**
  * Eliminate database queries and local memory cache lookups during endpoint authorization checks.
  * Keep JWT header footprint minimal (~280 bytes) using a compact 32-bit bitmask scalar.
  * Provide dual-read fallback in `PermissionAuthorizationHandler` for zero-downtime deployment.
  * Protect PostgreSQL connection pools by decoupling permission resolution from session row locks during token refresh.
  * Adjust rate limiting on `/api/v1/auth/refresh` to support multi-user NAT environments.
* **Out-of-Scope Non-Goals:**
  * Zero-delay (0 ms) instantaneous permission revocation (accepted trade-off: bounded to 5-minute token lifetime).
  * Removing `SessionValidationMiddleware` (session revocation, idle timeouts, and single-session enforcement remain active).

## Alternatives Weighed

1. **Option A: Embed Raw Permission Strings in 5-Minute Access Tokens**
   * *Status:* Rejected during brainstorm review.
   * *Reason:* Serializing 20 permission strings inflates the `Authorization` header to >1.5 KB, risking 431 proxy rejections, consuming unnecessary network bandwidth, and offering no technical advantage over a compact scalar bitmask.
2. **Option B: Extend In-Memory Cache TTL to 5 Minutes in PermissionAuthorizationHandler**
   * *Status:* Weighed as minimalist alternative.
   * *Reason:* While Option B requires only a one-line change and preserves 0 ms instant revocation via `IRbacInvalidationToken`, the user selected Option C to move authorization evaluation entirely into claims and eliminate in-process caching lookups during endpoint execution.
3. **Option C: Compact 32-Bit Capability Bitmask in 5-Minute Access Tokens (Selected)**
   * *Status:* Accepted for design drafting.
   * *Reason:* Resolves the header bloat contradiction while fulfilling the requirement to eliminate endpoint database and cache lookups.

## Key Decisions & Staging for Next Steps

* **Decision 1: Bitmask Schema:** Define a `PermissionBits` uint enum / mapper in `CsRaiders.Api.Domain` mapping all permissions from `PermissionCatalog` to powers of 2.
* **Decision 2: Token Service Interface:** Update `ITokenService.CreateAccessToken` to accept `uint permissionsBitmask` and emit claim `perms`.
* **Decision 3: Handler Dual-Read:** Update `PermissionAuthorizationHandler` to test bitmask claim first, falling back to existing `IMemoryCache` when absent.
* **Decision 4: Refresh Lock Decoupling:** In `RefreshTokenCommandHandler`, resolve user permissions prior to acquiring the pessimistic row lock on `sessions`.
* **Decision 5: Rate Limiter Configuration:** Partition the `/api/v1/auth` rate limiter to prevent NAT lockouts on refresh.
