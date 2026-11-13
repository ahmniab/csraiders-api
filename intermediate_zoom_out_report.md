# Stage 8 Intermediate Zoom Out Review Report

**Target:** [`docs/designs/token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md) (`lines 1–404`)  
**Standard:** [`resources/document_standards/zoom-out-standard.md`](file:///home/ahmniab/.gemini/config/plugins/egm/resources/document_standards/zoom-out-standard.md)  
**Report Destination:** `intermediate_zoom_out_report.md`

---

## Reviewer Status

| Reviewer | Status |
| --- | --- |
| `egm-zoom-out` | completed |

## Severity Breakdown
* 🔴 **Blocker**: 2
* 🟡 **Major**: 3
* 🟢 **Nit**: 3
* **Total Findings**: 8

---

## Prioritized Findings (Blockers -> Major -> Nits)

### [Refresh Pipeline Flawed Transaction Lifecycle & DoS Amplification] - Severity: Blocker
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 29, 60–63, 98–103, 219–228, 279, 340–342 (`CUJ 3`, `Technical Plan §1`, `Technical Plan §5`, `Detailed Implementation Plan §9`)
* **Discrepancy Description**:  
  The technical plan mandates "Pre-Lock Resolution", stating that [`RefreshTokenCommandHandler.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Application/Features/Auth/Commands/Refresh/RefreshTokenCommand.cs) will resolve user effective permissions from [`UserRepository`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Infrastructure/Persistence/Repositories/UserRepository.cs) and `RoleRepository` *prior* to acquiring the pessimistic row lock on `sessions`, asserting it will "commit transaction (< 2 ms)". This introduces two severe architectural defects:
  1. **Framework Lifecycle Conflict**: In [`ApplicationServiceExtensions.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Application/DependencyInjection/ApplicationServiceExtensions.cs#L39-L60), `TransactionBehavior<TMessage, TResponse>` controls database transactions ambiently across MediatR requests (`transactions.BeginAsync` before the handler, `transaction.CommitAsync` after). Handlers do not and cannot manually commit transactions. Pre-lock queries run inside the exact same ambient transaction.
  2. **Unauthenticated Query Amplification DoS**: Querying `users` and `roles` before verifying session existence (`session is null`), ownership (`session.UserId != claims.UserId`), and rotation validity (`session.TryRotate(...)`) enables anyone presenting an expired, forged, revoked, or replayed refresh token to trigger multi-table PostgreSQL joins before validating session legitimacy.
* **Rationale & Recommendation (Zoom-Out Standard §1 & §4)**:  
  Reorder the refresh sequence into a strict fail-fast pipeline:
  1. Cryptographically validate the refresh token and extract claims via `tokens.TryValidateRefreshToken`.
  2. Acquire the session row lock via `sessions.FindByIdForUpdateAsync(claims.SessionId, ct)`.
  3. Validate session ownership and execute rotation via `session.TryRotate(...)`. If rotation fails (`ReuseDetected`, `Inactive`, `DeviceMismatch`), fail immediately with zero permission queries.
  4. Only on `RefreshOutcome.Rotated`, resolve effective permissions. Holding the row lock across permission queries is safe because it locks only that specific single session row, causing zero contention for other users.
  5. Remove claims of manual handler commits and allow ambient `TransactionBehavior` to commit atomically.

---

### [Workload Inversion Anti-Pattern: Complete Cache Elimination] - Severity: Blocker
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 15–16, 19, 21, 27–28, 98–103, 179–181, 220–222 (`Overview`, `The Problem §1 & §3`, `Goals`, `Technical Plan §4 & §5`)
* **Discrepancy Description**:  
  To eliminate an in-memory cache lookup (~50 ns dictionary read) during endpoint authorization checks, the design eliminates memory caching entirely from the authorization subsystem while shortening access token lifetime to 5 minutes.
  Under this architecture, every token refresh (every 5 minutes per active user) executes `users.FindByIdWithPermissionsAsync(userId)` and `roles.FindByIdsAsync(...)` directly against PostgreSQL.
  For 10,000 active users, this imposes ~67 multi-table relational joins per second continuously on PostgreSQL. This is a classic **Workload Inversion Anti-Pattern** (Zoom-Out Standard §2 & §4), trading a 50 ns memory read for continuous relational database load.
* **Rationale & Recommendation (Zoom-Out Standard §2 & §4)**:  
  Keep endpoint authorization checks purely CPU-bound and cache-free (evaluating the `"perms"` claim in [`PermissionAuthorizationHandler.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.API/Authorization/PermissionAuthorizationHandler.cs)), but retain `IMemoryCache` backed by `IRbacInvalidationToken` inside [`RefreshTokenCommandHandler.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Application/Features/Auth/Commands/Refresh/RefreshTokenCommand.cs).
  During refresh, fetch the user's bitmask from memory cache (< 50 ns). Only on cache miss (e.g. server restart) or when an administrative mutation invalidates `IRbacInvalidationToken` should PostgreSQL be queried. This eliminates 99%+ of database queries during token refresh.

---

### [Multi-User NAT Rate Limiting Fragility & Ineffective Partitioning] - Severity: Major
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 22–23, 33, 85, 232–237, 370–373 (`The Problem §4`, `Goals`, `Technical Plan §1 & §6`, `Detailed Implementation Plan §16`)
* **Discrepancy Description**:  
  Increasing the fixed-window IP rate limiter on `/api/v1/auth/refresh` from 20 to 100 req/min is a localized band-aid that still caps NAT gateway capacity at ~450–500 active users:
  $$\text{NAT Ceiling} = \frac{100\text{ req/min}}{0.22\text{ refreshes/min/user}} \approx 450\text{ users}$$
  In campus, corporate office, or cellular CGNAT networks with $> 500$ users behind a shared IP, background refreshes will exceed 100 req/min, triggering cascading HTTP 429 lockouts and session drops.
  Furthermore, rate limiting refresh requests by IP address treats authenticated, cryptographically signed session tokens as unauthenticated traffic.
* **Rationale & Recommendation (Zoom-Out Standard §1 & §2)**:  
  Extract rate limiting from the network layer (IP) and partition `/api/v1/auth/refresh` by `SessionId` (extracted from the refresh token or session cookie). Allow 5–10 requests/minute per session, which prevents single-client refresh loops while completely decoupling multi-user NAT networks from shared IP ceilings. Retain strict IP rate limiting (20 req/min) for unauthenticated `/api/v1/auth/login` and `/api/v1/auth/register`.

---

### [Signed vs. Unsigned 64-Bit Integer Bitmask Modeling (`long` vs. `ulong`)] - Severity: Major
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 40, 118–145, 189, 302–305 (`Non-Goals`, `Technical Plan §2 & §4`, `Detailed Implementation Plan §1`)
* **Discrepancy Description**:  
  `PermissionBits` is declared as `[Flags] public enum PermissionBits : long`. If the catalog expands beyond 62 flags up to 64 flags, setting bit 63 causes the value to become negative in signed two's complement (`-9223372036854775808`).
  When serialized as a string claim, negative integer strings introduce two's complement sign-extension anomalies and confusion across JavaScript (`BigInt`), Python, and edge gateways, creating an architectural dead-end (Zoom-Out Standard §2).
* **Rationale & Recommendation (Zoom-Out Standard §2)**:  
  Declare `PermissionBits : ulong` (`uint64`), and parse via `ulong.TryParse(permsClaim.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bitmask)`. Unsigned integers accurately represent 64 independent capability bits (0 to 63) and serialize to clean positive decimal strings up to $18,446,744,073,709,551,615$.

---

### [Administrative Privilege Revocation Propagation Window] - Severity: Major
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 31, 37, 270–272 (`Goals`, `Non-Goals`, `Non-Functional Requirements §Security & Privacy`)
* **Discrepancy Description**:  
  The document accepts a 5.5-minute delay (5-minute token + 30s clock skew) for administrative permission revocations as an unavoidable trade-off. During this window, an administratively demoted or compromised user retains valid access tokens authorizing sensitive actions (`role:manage`, `payment:refund`, `user:write`, `session:revoke`).
  However, [`SessionValidationMiddleware.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.API/Middlewares/SessionValidationMiddleware.cs) already inspects session state on every request with a 5-second cache.
* **Rationale & Recommendation (Zoom-Out Standard §4 Leverage Existing)**:  
  Mandate that administrative commands modifying sensitive roles or revoking critical permissions (e.g. in `RbacCommands.cs`) invoke `ISessionRepository.RevokeAllByUserIdAsync(targetUserId)`. This terminates active sessions within 5 seconds via existing middleware without compromising endpoint authorization latency.

---

### [Implementation Plan Task Bloat: Standalone README Tasks] - Severity: Nit
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 318–321, 327–330, 335–338, 343–346, 351–354, 366–369, 374–377 (`Detailed Implementation Plan`, Tasks 4, 6, 8, 10, 12, 15, 17)
* **Discrepancy Description**:  
  The implementation plan lists 7 separate standalone tasks for modifying directory `README.md` files. This clutters the plan and obscures primary technical deliverables (Zoom-Out Standard §3 Anti-Weeds).
* **Rationale & Recommendation**:  
  Consolidate each documentation update into its corresponding parent code modification task (e.g., merge Task 4 into Tasks 1–3, Task 6 into Task 5, etc.).

---

### [Sub-Nanosecond Latency Claim Calibration] - Severity: Nit
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 13, 52, 94, 215, 277 (`Overview`, `CUJ 1`, `Technical Plan §1 & §4`, `Performance & Latency`)
* **Discrepancy Description**:  
  The document repeatedly asserts that authorization checks complete in "< 1 nanosecond". Claim iteration, string parsing via `TryParse`, and method dispatch in managed code take ~20–50 nanoseconds. Claiming sub-nanosecond latency for the entire handler pipeline is technically unrealistic.
* **Rationale & Recommendation**:  
  Calibrate the claims from "< 1 nanosecond" to "sub-microsecond (< 50 nanoseconds)" to maintain technical credibility and precision.

---

### [Structural Heading Hierarchy Violation for Trailing Sections] - Severity: Nit
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** Lines 287, 293 (`### Additional Resources`, `### Future Improvements`)
* **Discrepancy Description**:  
  Following the horizontal divider at Line 285 concluding `## Non-Functional Requirements`, `Additional Resources` (Line 287) and `Future Improvements` (Line 293) are rendered as H3 subsections rather than top-level H2 sections, incorrectly nesting them under `## Non-Functional Requirements`.
* **Rationale & Recommendation**:  
  Promote both sections to top-level H2 headings:
  - `## Additional Resources`
  - `## Future Improvements`
