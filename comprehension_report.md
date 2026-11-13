# Stage 6 Comprehension Test Report

**Target:** [`docs/designs/token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md) (`lines 1–404`)  
**Standard:** Comprehension Test (Self-Containment & Naive Reader)  
**Report Destination:** `comprehension_report.md`

---

## Paraphrase

### What this proposes
The document proposes encoding user authorization permissions into a 64-bit integer bitmask formatted as a decimal string inside a `"perms"` claim in JWT access tokens. Endpoint authorization checks (`[HasPermission]`) parse this claim and verify permissions via sub-nanosecond bitwise operations in memory, failing immediately without falling back to database or cache queries if the claim is missing or invalid. Access token lifetime is shortened to 5 minutes to bound administrative permission revocation lag, while refresh token handling pre-resolves user permissions outside the pessimistic row lock on `sessions`. String-based permission storage in PostgreSQL is retained to avoid database schema migrations and preserve audit logs, and rate limiting on `/api/v1/auth/refresh` is raised to 100 req/min per IP to mitigate NAT gateway bottlenecks.

### How the existing system works, per this document
Currently, every endpoint decorated with `[HasPermission]` triggers `PermissionAuthorizationHandler`, which constructs a cache key (`perms:{userId}`) and queries `IMemoryCache`. On cache misses or invalidations, it executes split queries across the `users`, `user_roles`, and `user_permissions` tables in PostgreSQL. Access tokens currently live for 15 minutes, and permissions are not embedded within token claims due to the 1.5+ KB header bloat caused by serializing 20 permission strings. Token refresh in `RefreshTokenCommandHandler` acquires an exclusive pessimistic row lock (`SELECT ... FOR UPDATE` on `sessions`), and all `/api/v1/auth` routes share a fixed-window rate limiter of 20 requests per minute per IP.

---

## Prioritized Findings

### [Unstated Refresh Token Validation Prerequisite] - Severity: Major
* **Reviewer Lens:** `egm-comprehension`
* **Affected Scope:** Section CUJ 3 (lines 59–63), Section 1 Architecture Diagram (lines 98–103), and Section 5.1 (lines 219–228)
* **Discrepancy Description & Rationale**:  
  CUJ 3 and the refresh architecture diagram specify that effective permissions are resolved *prior* to acquiring the pessimistic row lock on `sessions` ("1. Resolve Effective Permissions ... 3. Acquire Session Row Lock"). Section 5.1 states: "Extract `claims.UserId` and query effective permissions...".  
  For a fresh reader who does not already know that refresh tokens are signed JWTs, it is unclear how the handler obtains `UserId` before querying the `sessions` table. The document omits Step 0: cryptographically validating and decoding the refresh token via `ITokenService.TryValidateRefreshToken` to extract `claims.UserId` before hitting the database.

---

### [Omission of Domain Permission Aggregation & Constructor Injections] - Severity: Major
* **Reviewer Lens:** `egm-comprehension`
* **Affected Scope:** Section 5 ("Decoupled Token Refresh & Login Pipeline", lines 219–231) and Section Detailed Implementation Plan (Items 7 & 9, lines 331–345)
* **Discrepancy Description & Rationale**:  
  The document states that `RefreshTokenCommandHandler` and `LoginCommandHandler` will "query effective permissions via existing repository methods (`users.FindByIdWithPermissionsAsync` and `roles.FindByIdsAsync`)" and then "aggregate effective permission strings into `long bitmask` via `PermissionBitmaskMapper.ToBitmask`."  
  However, `users.FindByIdWithPermissionsAsync` returns a `User` entity (with `Roles` and `PermissionOverrides`), and `roles.FindByIdsAsync` returns `IReadOnlyList<Role>`. In the domain model, combining role permissions with user grant/deny overrides is executed by [`EffectivePermissions.Resolve(assignedRoles, user.PermissionOverrides)`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Policies/EffectivePermissions.cs). The document skips this domain policy entirely, leaving an unexplained gap between repository queries and `PermissionBitmaskMapper.ToBitmask`.  
  Furthermore, neither command handler currently injects both repositories (`RefreshTokenCommandHandler` has neither `IUserRepository` nor `IRoleRepository`; `LoginCommandHandler` lacks `IRoleRepository`). The implementation plan fails to specify injecting these repositories into the command handler constructors.

---

### [Inaccurate Solution and Unit Test Project Paths] - Severity: Major
* **Reviewer Lens:** `egm-comprehension`
* **Affected Scope:** Section Detailed Implementation Plan (Items 18 & 19, lines 378–389) and Verification Plan (lines 392–396)
* **Discrepancy Description & Rationale**:  
  The document directs the reader to:
  1. Create `tests/CsRaiders.Api.UnitTests/Domain/PermissionBitmaskMapperTests.cs` and run `dotnet test tests/CsRaiders.Api.UnitTests/CsRaiders.Api.UnitTests.csproj`. The directory `tests/CsRaiders.Api.UnitTests` does not exist; unit tests reside in [`tests/CsRaiders.Api.Domain.UnitTests`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/tests/CsRaiders.Api.Domain.UnitTests) and [`tests/CsRaiders.Api.Application.UnitTests`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/tests/CsRaiders.Api.Application.UnitTests).
  2. Modify `tests/CsRaiders.Api.UnitTests/API/PermissionAuthorizationHandlerTests.cs`. This file does not exist anywhere in the repository, making `(Modify)` inaccurate (should be `(Create)`).
  3. Build `src/CsRaiders.Api.sln`. The solution file is located at the root ([`CsRaiders.Api.sln`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/CsRaiders.Api.sln)), not in `src/`.

---

### [Incomplete Branch Representation in Authorization Flowchart] - Severity: Nit
* **Reviewer Lens:** `egm-comprehension`
* **Affected Scope:** Section 1 Architecture Diagram ("EvaluationPath", lines 91–96, 107–108)
* **Discrepancy Description & Rationale**:  
  In the Mermaid diagram, the flowchart links `CheckClaim -- Yes --> ParseInt --> BitwiseCheck --> Endpoints["Controller Action"]` directly. The only branch to `FailFast` comes from `CheckClaim -- No / Invalid`. There is no outgoing failure branch from `BitwiseCheck` when `(bitmask & requiredBit) == 0`.

---

### [Undefined Lifecycle for Removed Handler Dependencies] - Severity: Nit
* **Reviewer Lens:** `egm-comprehension`
* **Affected Scope:** Section 4 ("Endpoint Authorization Handler", line 180)
* **Discrepancy Description & Rationale**:  
  Line 180 specifies dropping `IRbacInvalidationToken` and `IAuthMetricsRecorder` from `PermissionAuthorizationHandler`. Clarifying that cache hit/miss metrics are removed because evaluation is pure CPU, and noting that `IRbacInvalidationToken` is no longer needed by authorization due to bounded 5-minute token expiry, avoids confusion.

---

### [Ambiguity on Other Authentication Routes Under `/api/v1/auth`] - Severity: Nit
* **Reviewer Lens:** `egm-comprehension`
* **Affected Scope:** Section 6 ("Rate Limiting Partitioning for NAT Networks", lines 234–237) and Section 16 (lines 371–373)
* **Discrepancy Description & Rationale**:  
  The document specifies `20 req/min` for `/login` and `/register`, and `100 req/min` for `/refresh`, but does not explicitly state that remaining authentication endpoints (e.g. `/logout`, `/verify-email`) retain the default `20 req/min` policy.
