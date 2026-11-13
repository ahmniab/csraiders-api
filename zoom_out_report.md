# Stage 5 Initial Zoom Out & Inventive Catalyst Review Report

**Target:** [`docs/designs/token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md) (`lines 1–404`)  
**Standard:** [`resources/document_standards/zoom-out-standard.md`](file:///home/ahmniab/.gemini/config/plugins/egm/resources/document_standards/zoom-out-standard.md)  
**Report Destination:** `zoom_out_report.md`

---

## Reviewer Status

| Reviewer | Status |
| --- | --- |
| `egm-zoom-out` | completed |
| `egm-inventive-catalyst` | completed |

---

## Executive Synthesis

The joint audit evaluated the architectural integrity, simplicity, and trade-off balance of [`token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md) across lines 1–404.

While the design successfully resolved prior defects (aligning the 20 enum bits with [`PermissionCatalog.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Constants/PermissionCatalog.cs) and standardizing on decimal integer string claims), the strategic review surfaced two critical blockers and three major architectural liabilities:

1. **Transaction Lifecycle Conflict & DoS Amplification (🔴 Blocker)**: The technical plan specifies querying permissions *prior* to session locking and asserts that `RefreshTokenCommandHandler` will "commit transaction (< 2 ms)". In reality, ambient `TransactionBehavior` wraps the entire command, so manual commits violate framework architecture. Crucially, querying `users` and `roles` before verifying the session row or rotation (`session.TryRotate`) creates an unauthenticated DoS amplification vector against PostgreSQL.
2. **Workload Inversion Anti-Pattern (🔴 Blocker)**: To eliminate a ~50 ns in-memory cache lookup on endpoints, the design completely discards caching while reducing token lifetime to 5 minutes, forcing every single client refresh to execute heavy multi-table joins against PostgreSQL. For 10,000 active users, this shifts traffic from 50 ns memory reads to ~67 multi-table queries/sec against PostgreSQL.
3. **NAT Rate Limiting Fragility (🟡 Major)**: Bumping the fixed-window IP rate limiter to 100 req/min is a fragile local patch that still caps NAT gateways at 500 active users. Refresh rate limiting must be partitioned by `SessionId`.
4. **Administrative De-escalation Window (🟡 Major)**: Accepting a 5.5-minute delay on privilege demotion leaves high-impact operations exposed; commands modifying sensitive permissions must invoke `ISessionRepository.RevokeAllByUserIdAsync` to leverage `SessionValidationMiddleware.cs` for 5-second termination.
5. **Signed Integer Modeling Fragility (🟡 Major)**: Using signed `long` rather than unsigned `ulong` risks two's complement negative integer serialization if bit 63 is allocated.

---

## Prioritized Findings (Blockers -> Major -> Nits)

### [Refresh Pipeline DoS Amplification & Flawed Transaction Lifecycle] - Severity: Blocker
* **Reviewer Lens:** `egm-zoom-out` & `egm-inventive-catalyst`
* **Affected Scope:** Lines 29, 60–63, 98–103, 219–228, 279, 340–342
* **Discrepancy Description**:  
  The technical plan dictates resolving user permissions from PostgreSQL (`UserRepository` and `RoleRepository`) *prior* to session validation and locking, asserting that `RefreshTokenCommandHandler` will *"commit transaction (< 2 ms)"*.
  This introduces two severe architectural defects:
  1. **Framework Lifecycle Conflict**: In [`ApplicationServiceExtensions.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Application/DependencyInjection/ApplicationServiceExtensions.cs#L39-L60), `TransactionBehavior<TMessage, TResponse>` controls transaction boundaries ambiently across MediatR requests (`transactions.BeginAsync` before the handler, `transaction.CommitAsync` after). Handlers do not and cannot manually commit the transaction. All queries inside the handler are already part of the ambient transaction.
  2. **Unauthenticated DoS Amplification Vector**: Anyone presenting an expired, revoked, or replayed refresh token whose cryptographic signature is valid will force PostgreSQL to execute multi-table joins across `users`, `user_roles`, and `roles` *before* the handler even checks if the session exists or calls `session.TryRotate(...)`.
* **Rationale & Recommendation**:  
  Reorder the refresh sequence into a strict fail-fast pipeline:
  1. Verify cryptographic token signature and extract claims.
  2. Retrieve the session via `sessions.FindByIdForUpdateAsync(claims.SessionId, ct)` and execute `session.TryRotate(...)`. If rotation fails (`ReuseDetected`, `Inactive`, `DeviceMismatch`), fail immediately with zero permission queries.
  3. Only on confirmed successful rotation, resolve effective permissions.
  4. Allow ambient `TransactionBehavior` to commit the transaction atomically.

---

### [Workload Inversion & Premature Optimization: Database Read Storm] - Severity: Blocker
* **Reviewer Lens:** `egm-zoom-out` & `egm-inventive-catalyst`
* **Affected Scope:** Lines 15–16, 19, 21, 27–28, 98–103, 179–181, 220–222
* **Discrepancy Description**:  
  To eliminate an `IMemoryCache` lookup (a sub-microsecond dictionary read taking ~50 ns) on endpoint authorization checks, the design eliminates memory caching entirely from the authorization subsystem while simultaneously reducing access token lifetime to 5 minutes.
  Under this architecture, every token refresh (every 5 minutes per active user) executes `users.FindByIdWithPermissionsAsync(userId)` and `roles.FindByIdsAsync(...)` directly against PostgreSQL.
  For 10,000 active users, this imposes ~67 multi-table relational joins per second continuously on PostgreSQL. This is a textbook **Workload Inversion Anti-Pattern** (Zoom-Out Standard §2 & §4) and accepts a false compromise between endpoint speed and database health.
* **Rationale & Recommendation (TRIZ Pattern: Preliminary Action / Memoization)**:  
  Keep endpoint authorization checks purely CPU-bound and cache-free (evaluating the `"perms"` claim in [`PermissionAuthorizationHandler.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.API/Authorization/PermissionAuthorizationHandler.cs)), but retain `IMemoryCache` backed by `IRbacInvalidationToken` inside [`RefreshTokenCommandHandler.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Application/Features/Auth/Commands/Refresh/RefreshTokenCommand.cs) (or an application-level permission cache service).
  During refresh, fetch the user's bitmask from memory cache (< 50 ns). Only on cache miss (e.g. server restart) or when an administrative mutation invalidates the token should PostgreSQL be queried. This eliminates 99%+ of database queries during token refresh.

---

### [Multi-User NAT Rate Limiting Fragility & Ineffective Partitioning] - Severity: Major
* **Reviewer Lens:** `egm-zoom-out` & `egm-inventive-catalyst`
* **Affected Scope:** Lines 22–23, 33, 85, 232–237, 370–373
* **Discrepancy Description**:  
  Increasing the fixed-window IP rate limiter on `/api/v1/auth/refresh` from 20 to 100 req/min is a localized band-aid that still caps NAT gateway capacity at 500 active users:
  $$\text{NAT Ceiling} = \frac{100\text{ req/min}}{0.2\text{ refreshes/min/user}} = 500\text{ users}$$
  In campus or corporate environments with $> 500$ users behind a shared NAT, background refreshes will exceed 100 req/min, triggering cascading HTTP 429 lockouts and token drops.
  Furthermore, rate limiting refresh requests by IP address treats authenticated, cryptographically signed session tokens as unauthenticated traffic.
* **Rationale & Recommendation (TRIZ Pattern: Segmentation / Extraction)**:  
  Extract rate limiting from the network layer (IP) and partition `/api/v1/auth/refresh` by `SessionId` (extracted from the refresh token or session cookie). Allow 5–10 requests/minute per session, which prevents single-client refresh loops while completely decoupling multi-user NAT networks from shared IP ceilings. Retain strict IP rate limiting (20 req/min) for unauthenticated `/api/v1/auth/login` and `/api/v1/auth/register`.

---

### [Unmitigated Administrative Privilege De-escalation Window] - Severity: Major
* **Reviewer Lens:** `egm-zoom-out` & `egm-inventive-catalyst`
* **Affected Scope:** Lines 31, 37, 270–272
* **Discrepancy Description**:  
  The document accepts a 5.5-minute delay (5-minute token + 30s clock skew) for administrative permission revocations as an unavoidable law of JWTs, categorizing immediate de-escalation as a Non-Goal.
  During this 5.5-minute window, an administratively demoted or compromised user retains valid access tokens authorizing sensitive actions (`role:manage`, `payment:*`, `course:purge`).
* **Rationale & Recommendation (TRIZ Pattern: Blessing in Disguise / Beforehand Cushioning)**:  
  Leverage existing infrastructure! `SessionValidationMiddleware.cs` already inspects session state on every request with a 5-second cache. Require RBAC management commands that alter user roles or revoke permissions to invoke `ISessionRepository.RevokeAllByUserIdAsync(userId)`. This immediately terminates active sessions within 5 seconds via existing middleware without compromising endpoint authorization latency.

---

### [Signed vs. Unsigned 64-Bit Integer Bitmask Modeling (`long` vs. `ulong`)] - Severity: Major
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 40, 118–144, 189, 302–305
* **Discrepancy Description**:  
  `PermissionBits` is declared as `[Flags] public enum PermissionBits : long`. If the catalog expands beyond 62 flags up to 64 flags, setting bit 63 causes the value to become negative in signed two's complement (`-9223372036854775808`).
  When serialized as a string claim, negative integer strings introduce two's complement sign-extension anomalies and confusion across JavaScript (`BigInt`), Python, and edge gateways.
* **Rationale & Recommendation**:  
  Declare `PermissionBits` as `ulong` (`uint64`), and parse via `ulong.TryParse(..., NumberStyles.None, CultureInfo.InvariantCulture, out var bitmask)`. Unsigned integers accurately represent 64 independent capability bits (0 to 63) and serialize to clean positive decimal strings up to $18,446,744,073,709,551,615$.

---

### [Implementation Plan Task Bloat: Redundant Documentation Tasks] - Severity: Nit
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 318–321, 327–330, 335–338, 343–346, 351–354, 366–369, 374–377
* **Discrepancy Description**:  
  The implementation plan lists 7 separate standalone tasks (Tasks 4, 6, 8, 10, 12, 15, 17) for modifying directory `README.md` files, cluttering the plan and obscuring primary technical deliverables.
* **Rationale & Recommendation**:  
  Consolidate each documentation update into its corresponding parent code modification task (e.g., merge Task 4 into Tasks 1–3, Task 6 into Task 5, etc.).

---

### [Sub-Nanosecond Claim Precision Calibration] - Severity: Nit
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 13, 52, 94, 215, 277
* **Discrepancy Description**:  
  The document repeatedly asserts that authorization checks complete in "< 1 nanosecond". Claim extraction, string parsing via `TryParse`, and bitwise comparison in managed code take ~15–30 nanoseconds.
* **Rationale & Recommendation**:  
  Calibrate the claims from "< 1 nanosecond" to "sub-microsecond (< 50 nanoseconds)" to maintain technical credibility and precision.

---

### Summary Breakdown
* **🔴 Blockers:** 2
* **🟡 Majors:** 3
* **🟢 Nits:** 2
* **Total Findings:** 7
