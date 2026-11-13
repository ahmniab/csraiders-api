# Stage 9 Implementation Readiness Review Report

**Target:** `docs/designs/token_embedded_authorization_bitmask_claims.md` (`lines 298–404`)  
**Standard:** [`resources/document_standards/implementation-readiness-standard.md`](file:///home/ahmniab/.gemini/config/plugins/egm/resources/document_standards/implementation-readiness-standard.md)  
**Report Destination:** `implementation_readiness_report.md`

---

## Reviewer Status

| Reviewer | Status |
| --- | --- |
| `egm-implementation-readiness` | completed |
| `egm-codebase-details` | completed |
| `egm-style` | completed |

## Severity Breakdown
* 🔴 **Blocker**: 2
* 🟡 **Major**: 5
* 🟢 **Nit**: 3
* **Total Findings**: 10

---

## Prioritized Findings (Blockers -> Major -> Nits)

### [Fictitious Test Project and Invalid `(Modify)` Target] - Severity: Blocker
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** Lines 378–389, 394–395 (`Detailed Implementation Plan §18, §19, Verification Plan`)
* **Discrepancy Description**:
  The implementation plan references test project `tests/CsRaiders.Api.UnitTests/CsRaiders.Api.UnitTests.csproj`, which does not exist in the solution or on the filesystem. Executing the verification command `dotnet test tests/CsRaiders.Api.UnitTests/CsRaiders.Api.UnitTests.csproj` fails immediately with `MSB1009: Project file does not exist`.
  Furthermore, Item 19 marks `PermissionAuthorizationHandlerTests.cs` as `(Modify)`, but this test file does not exist anywhere in the repository.
  The actual test projects in `CsRaiders.Api.sln` are:
  - [`tests/CsRaiders.Api.Domain.UnitTests`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/tests/CsRaiders.Api.Domain.UnitTests)
  - [`tests/CsRaiders.Api.Application.UnitTests`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/tests/CsRaiders.Api.Application.UnitTests)
  - [`tests/CsRaiders.Api.IntegrationTests`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/tests/CsRaiders.Api.IntegrationTests)
  - [`tests/CsRaiders.Api.ArchitectureTests`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/tests/CsRaiders.Api.ArchitectureTests)
* **Rationale & Recommendation**:
  Update all test file targets and verification instructions to match the real solution architecture:
  1. Item 18: `tests/CsRaiders.Api.Domain.UnitTests/PermissionBitmaskMapperTests.cs` (Create).
  2. Item 19: `tests/CsRaiders.Api.IntegrationTests/PermissionAuthorizationHandlerTests.cs` (Create).
  3. Verification Plan: Update the unit test verification command to `dotnet test CsRaiders.Api.sln` or run project-specific test runs (`dotnet test tests/CsRaiders.Api.Domain.UnitTests/CsRaiders.Api.Domain.UnitTests.csproj`, etc.).

---

### [Pre-Lock Permission Resolution and Transaction Lifecycle Contradiction] - Severity: Blocker
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Lines 339–342 (`Detailed Implementation Plan §9`)
* **Discrepancy Description**:
  Item 9 instructs [`RefreshTokenCommandHandler`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Application/Features/Auth/Commands/Refresh/RefreshTokenCommand.cs) to resolve user effective permissions from repositories *before* acquiring the pessimistic row lock via `sessions.FindByIdForUpdateAsync`, claiming this will "execute session rotation in < 2 ms". This introduces two critical defects:
  1. **Framework Lifecycle Conflict**: In `ApplicationServiceExtensions.cs`, MediatR pipeline `TransactionBehavior<TMessage, TResponse>` controls ambient database transactions. Handlers do not and cannot manually commit transactions. Pre-lock queries execute within the exact same ambient transaction as the subsequent row lock.
  2. **Unauthenticated Query Amplification DoS**: Querying `users` and `roles` prior to verifying session existence (`session is null`), ownership (`session.UserId != claims.UserId`), and rotation legitimacy (`session.TryRotate(...)`) allows an attacker presenting invalid, expired, revoked, or replayed refresh tokens to force multi-table PostgreSQL joins.
* **Rationale & Recommendation**:
  Invert the refresh pipeline to enforce a strict fail-fast execution order:
  1. Validate refresh token cryptographically and extract claims (`tokens.TryValidateRefreshToken`).
  2. Acquire pessimistic row lock (`sessions.FindByIdForUpdateAsync(claims.SessionId, ct)`).
  3. Validate session ownership and execute rotation (`session.TryRotate(...)`). On rotation rejection (`DeviceMismatch`, `ReuseDetected`, `Inactive`), return immediately with zero repository queries.
  4. Only on `RefreshOutcome.Rotated`, resolve effective permissions. The row lock isolates only that specific single session row, creating zero concurrency bottlenecks for other users.
  5. Allow the ambient MediatR `TransactionBehavior` to commit the transaction atomically upon handler completion.

---

### [Missing Dependency Injections and Pipeline Boundaries in Command Handlers] - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Lines 331–334, 339–342 (`Detailed Implementation Plan §7, §9`)
* **Discrepancy Description**:
  Neither [`LoginCommandHandler`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Application/Features/Auth/Commands/Login/LoginCommand.cs) nor [`RefreshTokenCommandHandler`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Application/Features/Auth/Commands/Refresh/RefreshTokenCommand.cs) currently inject the necessary dependencies to resolve role-based permissions:
  - `LoginCommandHandler` injects `IUserRepository users`, but does not inject `IRoleRepository`. Furthermore, `users.FindByEmailAsync` loads only the user record without navigation collections (`Roles`, `PermissionOverrides`). The plan does not specify calling `users.FindByIdWithPermissionsAsync(user.Id, ct: ct)`. It also refers to `_tokenService.CreateAccessToken`, whereas the constructor parameter is named `tokens`.
  - `RefreshTokenCommandHandler` currently injects neither `IUserRepository` nor `IRoleRepository`.
  Leaving the constructor signatures, repository calls, and integration with `EffectivePermissions.Resolve` unspecified violates the Zero-Decision Rule.
* **Rationale & Recommendation**:
  Explicitly detail the constructor parameters and method invocations for each command:
  - In `LoginCommandHandler`: Add `IRoleRepository roles` to primary constructor. After credential verification and session lock, invoke `await users.FindByIdWithPermissionsAsync(user.Id, ct: ct)`, `await roles.FindByIdsAsync(user.Roles.Select(r => r.RoleId), ct)`, resolve permission strings via `EffectivePermissions.Resolve(assignedRoles, user.PermissionOverrides)`, map via `PermissionBitmaskMapper.ToBitmask(...)`, and pass scalar to `tokens.CreateAccessToken(user.Id, session.Id, bitmask, now)`.
  - In `RefreshTokenCommandHandler`: Add `IUserRepository users` and `IRoleRepository roles` to primary constructor. On `RefreshOutcome.Rotated`, query `users.FindByIdWithPermissionsAsync(session.UserId, ct: ct)` and `roles.FindByIdsAsync(...)`, compute bitmask, and pass to `tokens.CreateAccessToken(session.UserId, session.Id, bitmask, now)`.

---

### [Omission of Application Handler Unit Tests] - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Lines 378–389 (`Detailed Implementation Plan §18, §19`)
* **Discrepancy Description**:
  The plan alters the core permission resolution and token generation mechanisms within `LoginCommandHandler` and `RefreshTokenCommandHandler`. However, it fails to specify any unit tests within `tests/CsRaiders.Api.Application.UnitTests/Auth/` to verify these handlers.
* **Rationale & Recommendation**:
  Add explicit test tasks:
  - Item 20: `tests/CsRaiders.Api.Application.UnitTests/Auth/LoginCommandHandlerTests.cs` (Create) to verify role and override resolution, bitmask computation, and passing the scalar into `CreateAccessToken`.
  - Item 21: `tests/CsRaiders.Api.Application.UnitTests/Auth/RefreshTokenCommandHandlerTests.cs` (Create) to verify fail-fast rejection before querying permissions, and proper bitmask issuance upon successful rotation.

---

### [Underspecified Edge Case Semantics in `PermissionBitmaskMapper`] - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Lines 306–313 (`Detailed Implementation Plan §2`)
* **Discrepancy Description**:
  The specification for `PermissionBitmaskMapper.cs` omits edge case handling semantics:
  - Does `GetBit(string permissionName)` throw `ArgumentException`/`KeyNotFoundException` or return `0UL` when an unknown or null permission string is supplied?
  - Does `ToBitmask(IEnumerable<string> permissionNames)` skip unrecognized strings or throw?
  - Does `ToPermissionNames(ulong bitmask)` ignore undefined high bits or throw?
  Leaving these decisions to the implementer risks inconsistent behavior, uncaught 500 exceptions, or open security evaluations.
* **Rationale & Recommendation**:
  Specify explicit fail-closed semantics:
  - `GetBit(string permissionName)` returns `0UL` (`PermissionBits.None`) for any unrecognized, empty, or null string.
  - `ToBitmask(IEnumerable<string> permissionNames)` filters out unrecognized permissions and aggregates known bits.
  - `ToPermissionNames(ulong bitmask)` converts only bits mapped to known permissions in `PermissionCatalog`.

---

### [Rate Limiting Partition Key and Configuration Ambiguity] - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Lines 370–373 (`Detailed Implementation Plan §16`)
* **Discrepancy Description**:
  Item 16 instructs modifying `CsRaidersApiServiceExtensions.cs` to maintain `20 req/min per IP` for login and add `100 req/min per IP` for `/api/v1/auth/refresh`. However, in ASP.NET Core `PartitionedRateLimiter.Create<HttpContext, string>`, if both partitions return the client IP address as the string partition key, they share the same rate-limiting bucket. The plan does not specify distinct partition keys or configuration bindings.
* **Rationale & Recommendation**:
  Specify the exact partitioned configuration:
  - For `context.Request.Path.Equals("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase)`, return partition key `$"refresh_{ip}"` configured with `PermitLimit = builder.Configuration.GetValue("RateLimit:RefreshPerMinutePerIp", 100)`.
  - For other `context.Request.Path.StartsWithSegments("/api/v1/auth")` requests, return partition key `$"auth_{ip}"` configured with `PermitLimit = builder.Configuration.GetValue("RateLimit:PerMinutePerIp", 20)`.

---

### [Data Type Discrepancy: Signed `long` vs. Unsigned `ulong`] - Severity: Major
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** Lines 302–305, 306–313, 322–326 (`Detailed Implementation Plan §1, §2, §5`)
* **Discrepancy Description**:
  The plan models `PermissionBits` as `[Flags] public enum PermissionBits : long`. In signed 64-bit integers, assigning the 64th bit (`1L << 63`) yields `-9223372036854775808`. Serializing this to decimal integer strings generates negative numbers, introducing two's complement sign inconsistencies in edge gateways and client parsers.
* **Rationale & Recommendation**:
  Change the underlying type to unsigned 64-bit integer (`ulong` / `uint64`):
  - Item 1: `[Flags] public enum PermissionBits : ulong` (`1UL << 0` through `1UL << 19`).
  - Item 2: `public static ulong ToBitmask(IEnumerable<string> permissionNames)` and `public static ulong GetBit(string permissionName)`.
  - Item 5: `string CreateAccessToken(Guid userId, Guid sessionId, ulong permissionsBitmask, DateTimeOffset now);`.

---

### [Implementation Task Bloat: Standalone README Tasks] - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** Lines 318–321, 327–330, 335–338, 343–346, 351–354, 366–369, 374–377 (`Detailed Implementation Plan §4, §6, §8, §10, §12, §15, §17`)
* **Discrepancy Description**:
  7 out of the 19 numbered tasks in the plan are isolated entries dedicated solely to editing directory `README.md` files. This clutters the plan and buries functional code changes under trivial documentation chores.
* **Rationale & Recommendation**:
  Consolidate documentation updates into their corresponding parent code modification tasks (e.g. fold Item 4 into Items 1–3, Item 6 into Item 5, etc.) to streamline tracking.

---

### [Aspirational Latency Claim in Task Specification] - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** Line 341 (`Detailed Implementation Plan §9`)
* **Discrepancy Description**:
  The phrase "execute session rotation in < 2 ms" is an uncalibrated performance claim inserted into an imperative implementation task description. Implementation tasks should specify concrete technical instructions rather than uncalibrated operational SLAs.
* **Rationale & Recommendation**:
  `CALIBRATE`: Remove "execute session rotation in < 2 ms" from Item 9 and focus the instruction entirely on state transition logic and bitmask token issuance.

---

### [`PermissionAuthorizationHandler` Synchronous Signature Precision] - Severity: Nit
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Lines 355–361 (`Detailed Implementation Plan §13`)
* **Discrepancy Description**:
  `PermissionAuthorizationHandler.HandleRequirementAsync` was previously an `async Task` method because of database queries. With the transition to CPU-bound bitmask claims extraction, retaining `async Task` without `await` triggers compiler warning `CS1998` ("This async method lacks 'await' operators...").
* **Rationale & Recommendation**:
  Explicitly instruct changing the method signature to `protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)` and returning `Task.CompletedTask`.
