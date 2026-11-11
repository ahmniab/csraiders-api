# Stage 8 Intermediate Zoom Out Review Report

* **Document:** [`docs/designs/token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md)
* **Target Scope:** Full File (Lines 1–363)
* **Reviewer:** Skeptical Architect (`egm-zoom-out`)
* **Standard:** `zoom-out-standard`

---

## 1. Executive Intermediate Altitude Assessment

Stage 8 re-evaluated the architectural integrity of the entire design document following the Comprehension and Critic reviews. 

The review surfaced four critical architectural blockers where the document is currently split against itself:
1. **Deadlock on Rate Limiting Strategy (Blocker)**: The author's note at line 43 (*"keep the rate limiting logic as exist"*) directly contradicts Section 6, Goal 6, Problem 4, and the architecture diagram. Retaining the 20 req/min per IP rate limiter on `/api/v1/auth` while reducing access token lifetime to 5 minutes guarantees that any campus, corporate office, or cellular NAT with $\ge 100$ active users will experience 100% quota exhaustion on passive background refreshes alone, triggering cascading HTTP 429 lockouts and session drops.
2. **Claim Encoding Duality (Blocker)**: The author's note at line 41 specifies decimal integer encoding, yet Section 3, Section 4, and Target File 11 specify standard string claim emission (`new Claim("perms", ToString(...))`) and string parsing (`uint.TryParse(permsClaim.Value)`). In ASP.NET Core `ClaimsIdentity`, claim values are strings; emitting raw JSON numeric values requires direct manipulation of `SecurityTokenDescriptor.Claims`.
3. **Persisting Domain Catalog Disconnect (Blocker)**: `PermissionBits` in lines 110–133 continues to declare 20 synthetic CRUD permissions (`UsersRead`, `AssessmentsGrade`, `CoursesDelete`) that do not exist in the domain model, while omitting over 10 actual production permissions from `PermissionCatalog.cs` (`course:read`, `course:purge`, `payment:verify`, `session:revoke`).
4. **Transaction Boundary & DoS Amplification in Refresh (Blocker)**: Proposing manual transaction commits inside `RefreshTokenCommandHandler` violates MediatR's ambient `TransactionBehavior<TMessage, TResponse>`. Furthermore, resolving permissions before verifying token signatures or session state creates an unauthenticated database query amplification vector against PostgreSQL.
5. **Workload Inversion (Major)**: Resolving permissions from PostgreSQL on every 5-minute refresh without caching trades lightweight in-memory cache lookups on endpoints for heavy multi-table database joins on the write path.

---

## 2. Prioritized Review Findings

### 🔴 Blocker Findings

#### [BLOCKER 1] Fatal Contradiction: Existing IP Rate Limiter vs. 5-Minute Token Lifetime on NATs
* **Affected Scope:** Lines 20, 30, 42–43, 78, 205–213, 348–355.
* **Details:**  
  Retaining the existing 20 req/min per IP limiter across all `/api/v1/auth` endpoints caps NAT network capacity at 100 active users:
  $$\text{NAT Capacity Ceiling} = \frac{20\text{ req/min}}{0.2\text{ refreshes/min/user}} = 100\text{ users}$$
  In an office or campus network with $\ge 100$ users, background refreshes alone consume 100% of the IP quota, causing routine API calls and refreshes to fail with HTTP 429, forcibly logging users out.
* **Minimalist Recommendation:**  
  *Option A*: Formally adopt the partitioned rate-limiting policy (100–120 req/min on `/api/v1/auth/refresh` or keyed by `SessionId`) to support 5-minute access tokens.  
  *Option B*: If rate limiting logic cannot be modified, retain the existing 15-minute token lifetime (`AuthRules.AccessTokenLifetime`) to prevent NAT starvation.

---

#### [BLOCKER 2] Claim Data Type Contradiction: Numeric JSON Scalar vs. String Claim
* **Affected Scope:** Lines 40–41, 150–155, 170–172, 328–331.
* **Details:**  
  The author specified a numeric integer claim, but the document body specifies string serialization (`new Claim("perms", ToString(...))`).
* **Minimalist Recommendation:**  
  Standardize on the standard string claim pattern (`new Claim("perms", permissionsBitmask.ToString(CultureInfo.InvariantCulture))`). This consumes only 7–10 bytes in the JWT payload, requires zero custom JSON serializer overrides in ASP.NET Core, and parses cleanly via `ulong.TryParse`. If raw JSON numbers are mandatory, explicitly specify `SecurityTokenDescriptor.Claims["perms"] = (long)permissionsBitmask`.

---

#### [BLOCKER 3] Fictional Domain Catalog Hallucination Persists
* **Affected Scope:** Lines 49, 54, 108–134, 278–283.
* **Details:**  
  `PermissionBits` defines synthetic permissions (`UsersRead`, `AssessmentsGrade`, etc.) instead of the 20 actual constants in `PermissionCatalog.cs` (`course:read`, `payment:verify`, `session:revoke`).
* **Minimalist Recommendation:**  
  Replace `PermissionBits` with `[Flags] public enum PermissionBits : ulong` mapped 1:1 to the 20 constants in `PermissionCatalog.cs`.

---

#### [BLOCKER 4] Transaction Lifecycle Conflict & DoS Amplification in Refresh Pipeline
* **Affected Scope:** Lines 29, 60, 90–95, 188–204, 320–323.
* **Details:**  
  `TransactionBehavior` controls transaction scope across MediatR commands. Attempting manual commits inside `RefreshTokenCommandHandler` causes runtime exceptions. Resolving permissions before validating the session row also creates a DoS vector.
* **Minimalist Recommendation:**  
  Align the refresh sequence:
  1. Cryptographically validate the refresh token first.
  2. Acquire session row lock via `sessions.FindByIdForUpdateAsync` and execute `session.TryRotate(...)`.
  3. Fetch the permission bitmask from `IMemoryCache` (keyed by `perms:{userId}` with `IRbacInvalidationToken`), falling back to database queries only on cache miss.
  4. Let `TransactionBehavior` commit atomically.

---

### 🟡 Major Findings

#### [MAJOR 1] Workload Inversion: Swapping Endpoint Memory Lookups for Refresh Database Joins
* **Affected Scope:** Lines 17–19, 90–95, 190–204.
* **Details:**  
  Resolving permissions from PostgreSQL on every 5-minute refresh triples database load. Reusing `IMemoryCache` during refresh eliminates database queries on 99% of token rotations.

#### [MAJOR 2] Unmitigated Administrative Privilege De-escalation Window
* **Affected Scope:** Lines 34, 62, 240–248.
* **Details:**  
  Embedding permissions in JWTs creates a 5.5-minute delay before administrative permission revocations take effect.
* **Minimalist Recommendation:**  
  Require RBAC commands modifying sensitive permissions (`payment:*`, `role:manage`, `user:write`) to call `ISessionRepository.RevokeAllByUserIdAsync`, leveraging `SessionValidationMiddleware` (5-second cache) for immediate de-escalation.

---

### 🟢 Nit Findings

* **[NIT 1] Task List Consolidation**: Fold the 7 standalone README tasks into their respective parent code modification tasks in the Detailed Implementation Plan.
* **[NIT 2] 64-Bit Headroom**: Back `PermissionBits` with `ulong` (64 bits) to provide 44 expansion slots with zero additional byte cost.
