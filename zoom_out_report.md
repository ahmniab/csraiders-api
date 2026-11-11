# Stage 5 Initial Zoom Out Review Report

* **Document:** [`docs/designs/token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md)
* **Date:** 2026-11-11
* **Reviewers:** Skeptical Architect (`egm-zoom-out`), Inventive Catalyst (`egm-inventive-catalyst`)

---

## Executive Summary

Stage 5 evaluated the high-altitude necessity, domain grounding, system contradictions, and simplicity of the proposed token-embedded authorization bitmask claims architecture. 

The review surfaced critical architectural conflicts:
1. **Domain Constant Mismatch (Blocker)**: The design document defines a synthetic list of permissions (`UsersRead`, `AssessmentsGrade`, `EnrollmentsManage`, etc.) that contradicts the actual domain constants defined in `CsRaiders.Api.Domain.Constants.PermissionCatalog` (`course:read`, `payment:verify`, `session:revoke`, etc.).
2. **Workload Inversion & Cobra Effect (Blocker)**: In the existing codebase, `RefreshTokenCommandHandler` only operates on the `sessions` table. Forcing permission resolution into the refresh pipeline—while tripling refresh frequency to 5 minutes—trades sub-microsecond in-memory endpoint cache reads for multi-table database read operations on PostgreSQL.
3. **De-escalation Security Lag (Major)**: Burning permissions into client tokens replaces the existing 0 ms immediate RBAC eviction (`IRbacInvalidationToken.Invalidate()`) with an unmitigated 5-minute propagation delay.
4. **NAT Rate Limiting Fragility (Major)**: A static 100 req/min IP rate limit on refresh remains vulnerable to cascading lockouts on large campus/office NATs while permitting abusive refresh bursts from single IPs.

---

## Prioritized Review Findings

### 🔴 Blocker: Domain Catalog Mismatch & Non-Existent Constants
* **Lens:** Skeptical Architect & Inventive Catalyst
* **Affected Scope:** `Section 2: Domain Model & Capability Bitmask Mapping`, `Section 4: CUJs`, `Section 7: Target Files`.
* **Details:**  
  The proposed flags enum `PermissionBits` defines permissions (`UsersRead`, `UsersCreate`, `RolesRead`, `CoursesRead`, `EnrollmentsManage`, `AssessmentsGrade`, `SystemMetricsRead`) that do not exist in the domain. In `PermissionCatalog.cs`, the actual permissions use lowercase colon-delimited naming:
  * Courses: `course:read`, `course:write`, `course:purge`, `lesson:write`, `asset:write`, `question:write`, `quiz:write`
  * Users & Roles: `user:read`, `user:write`, `role:manage`
  * Payments: `payment:read`, `payment:verify`, `payment:refund`
  * System/Audit: `notification:send`, `analytics:read`, `audit:read`, `settings:read`, `settings:write`
  * Sessions: `session:multi`, `session:revoke`
* **Remediation Required:** Align `PermissionBits` enum members and `PermissionBitmaskMapper` strictly with `PermissionCatalog.All`.

---

### 🔴 Blocker: Refresh Pipeline DB Read Contention & Defensive Order Inversion
* **Lens:** Skeptical Architect & Inventive Catalyst
* **Affected Scope:** `Section 5: Refresh Command Handler Optimization` and `Implementation Item 9`.
* **Details:**  
  Currently, `RefreshTokenCommandHandler.cs` performs zero database queries against `users` or `roles`. Shifting permission resolution to refresh forces split queries across `users`, `user_roles`, `user_permissions`, and `roles` on every 5-minute refresh. Moving resolution *before* the session lock also means expensive multi-table queries run before verifying token presentation, session expiration, or token replay reuse.
* **Remediation Required:**
  1. Validate refresh token cryptographic signature, presentation, and session active state *before* resolving permissions.
  2. Cache resolved user permission bitmasks in memory (invalidated via `IRbacInvalidationToken`) so token refresh reads the bitmask from memory in sub-microseconds rather than querying PostgreSQL on every 5-minute refresh.

---

### 🟡 Major: Static IP Rate Limiting Fails Large Multi-User NATs
* **Lens:** Inventive Catalyst & Skeptical Architect
* **Affected Scope:** `Section 6: Rate Limiting Partitioning for NAT Networks` and `Implementation Item 16`.
* **Details:**  
  Raising the IP rate limit from 20 to 100 req/min on `/api/v1/auth/refresh` remains a static compromise: a university or corporate network with >500 active users will still trigger cascading 429 lockouts, while a single client on a residential IP can hammer refresh 100 times per minute.
* **Remediation Required:**  
  Partition rate limiting by session credential (`SessionId` or `__Host-csraiders_rt` cookie) with a tight window (e.g. 5 req/min per session) alongside a broad IP flood guard (e.g. 1,000 req/min per IP).

---

### 🟡 Major: Administrative De-escalation Window Lag
* **Lens:** Skeptical Architect
* **Affected Scope:** `Goals`, `Non-Goals`, and `Security & Privacy`.
* **Details:**  
  Currently, `RbacCommands.cs` calls `invalidation.Invalidate()`, clearing in-memory authorization caches immediately across the process on role edits. Embedding permissions into 5-minute access tokens delays administrative privilege removal for up to 330 seconds (5m + 30s clock skew) for high-privilege operations (`course:purge`, `role:manage`, `payment:refund`).
* **Remediation Options:**
  1. Accept 5-minute lag as an explicit trade-off.
  2. Embed a 1-byte `PermissionsEpoch` in `SessionStatus` verified by `SessionValidationMiddleware.cs` (5s cache TTL) against a claim in the JWT.

---

### 🟢 Nit: 32-Bit Scalar Ceiling vs. Zero-Cost 64-Bit Space
* **Lens:** Inventive Catalyst
* **Affected Scope:** `Section 2: Domain Model` and `Future Improvements`.
* **Details:**  
  The catalog already has 20 permissions, leaving only 12 free bits in `uint`. In JSON payload serialization, a decimal integer string (`"1048575"`) consumes the exact same character count whether backed by `uint` (32-bit) or `ulong` (64-bit). Adopting `ulong` from Day 1 provides 44 expansion slots with zero byte overhead.

---

## Recommendations for Document Update

1. **Re-ground `PermissionBits` Enum**: Map enum members directly to `PermissionCatalog.cs` constants (`CourseRead`, `PaymentRefund`, `SessionRevoke`, etc.).
2. **Cache Bitmask at Refresh**: Update `RefreshTokenCommandHandler` to fetch the bitmask from an in-memory cache keyed by user ID, avoiding PostgreSQL split queries on every 5-minute rotation.
3. **Session-Keyed Rate Limiting**: Configure ASP.NET Core `PartitionedRateLimiter` to key on session identifier rather than raw IP for token refreshes.
4. **Upgrade to `ulong`**: Use 64-bit backing integer for `PermissionBits` to provide immediate future expansion.
