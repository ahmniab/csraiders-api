# Stage 9 Implementation Readiness Test Report

* **Document:** [`docs/designs/token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md)
* **Target Scope:** Lines 274–363 (`Detailed Implementation Plan`)
* **Reviewers:** Pragmatic Tech Lead (`egm-implementation-readiness`), Technical Quality Auditor (`egm-codebase-details`), Technical Copy Editor (`egm-style`)
* **Standard:** `re-implementation-standard`
* **Readiness Score:** 52 / 100 (NOT READY FOR IMPLEMENTATION — REMEDIATION REQUIRED)

---

## 1. Executive Readiness Assessment

Stage 9 audited the Detailed Implementation Plan to verify that an engineer can execute the specification without ambiguity, missing files, broken test references, or conflicting requirements.

The plan failed readiness standards due to four critical blockers:
1. **Fictional Domain Permissions (Blocker)**: `PermissionBits.cs` (Item 1) defines synthetic permissions (`UsersRead`, `AssessmentsGrade`, etc.) that do not exist in the codebase. It must be mapped directly to the 20 actual permissions in `CsRaiders.Api.Domain.Constants.PermissionCatalog` (`course:read`, `payment:verify`, `session:revoke`).
2. **Non-Existent Test Project and Fictitious `(Modify)` Target (Blocker)**: Items 18 & 19 reference `tests/CsRaiders.Api.UnitTests/`, a project that does not exist in the solution. Furthermore, `PermissionAuthorizationHandlerTests.cs` does not exist anywhere in the repository, making `(Modify)` an invalid instruction. The actual test projects are `CsRaiders.Api.Domain.UnitTests`, `CsRaiders.Api.Application.UnitTests`, and `CsRaiders.Api.IntegrationTests`.
3. **Internal Contradiction on Rate Limiting (Blocker)**: The author's note at line 43 states *"keep the rate limiting logic as exist"*, yet Items 16 & 17 instruct modifying `CsRaidersApiServiceExtensions.cs` to add a 100 req/min partition for `/refresh`.
4. **Missing Dependency Injections and Pipeline Boundaries in Command Handlers (Major)**:
   - `LoginCommandHandler` must inject `IRoleRepository` and call `users.FindByIdWithPermissionsAsync(user.Id, ct)` to resolve roles and overrides before computing the bitmask.
   - `RefreshTokenCommandHandler` must inject `IUserRepository` and `IRoleRepository`. It extracts `claims.UserId` from `tokens.TryValidateRefreshToken`, updates sessions synchronously via `ISessionRepository.Update(session)`, and lets MediatR's ambient `TransactionBehavior` commit the transaction.
5. **Omission of Application Handler Unit Tests (Major)**: `LoginCommandHandler` and `RefreshTokenCommandHandler` undergo major permission resolution modifications, yet the plan omits unit tests in `tests/CsRaiders.Api.Application.UnitTests/Auth/`.

---

## 2. Prioritized Implementation Gaps

### 🔴 Blocker Gaps

#### [BLOCKER 1] Realignment of `PermissionBits.cs` with `PermissionCatalog`
* **Affected Scope**: Item 1 (lines 278–283)
* **Details**: Replace synthetic enum members with exact constants from `PermissionCatalog.cs`:
  ```csharp
  [Flags]
  public enum PermissionBits : ulong
  {
      None             = 0,
      CourseRead       = 1UL << 0,  // "course:read"
      CourseWrite      = 1UL << 1,  // "course:write"
      CoursePurge      = 1UL << 2,  // "course:purge"
      LessonWrite      = 1UL << 3,  // "lesson:write"
      AssetWrite       = 1UL << 4,  // "asset:write"
      QuestionWrite    = 1UL << 5,  // "question:write"
      QuizWrite        = 1UL << 6,  // "quiz:write"
      UserRead         = 1UL << 7,  // "user:read"
      UserWrite        = 1UL << 8,  // "user:write"
      RoleManage       = 1UL << 9,  // "role:manage"
      PaymentRead      = 1UL << 10, // "payment:read"
      PaymentVerify    = 1UL << 11, // "payment:verify"
      PaymentRefund    = 1UL << 12, // "payment:refund"
      NotificationSend = 1UL << 13, // "notification:send"
      AnalyticsRead    = 1UL << 14, // "analytics:read"
      AuditRead        = 1UL << 15, // "audit:read"
      SettingsRead     = 1UL << 16, // "settings:read"
      SettingsWrite    = 1UL << 17, // "settings:write"
      SessionMulti     = 1UL << 18, // "session:multi"
      SessionRevoke    = 1UL << 19  // "session:revoke"
  }
  ```

---

#### [BLOCKER 2] Correction of Test Project Structure and File Paths
* **Affected Scope**: Items 18 & 19 (lines 356–363)
* **Details**: 
  - `tests/CsRaiders.Api.UnitTests` does not exist.
  - `PermissionAuthorizationHandlerTests.cs` does not exist anywhere in the repo.
* **Remediation**:
  - Item 18: `tests/CsRaiders.Api.Domain.UnitTests/PermissionBitmaskMapperTests.cs` (Create).
  - Item 19: `tests/CsRaiders.Api.IntegrationTests/PermissionAuthorizationHandlerTests.cs` (Create).
  - Add Item 20: `tests/CsRaiders.Api.Application.UnitTests/Auth/LoginCommandHandlerTests.cs` (Create).
  - Add Item 21: `tests/CsRaiders.Api.Application.UnitTests/Auth/RefreshTokenCommandHandlerTests.cs` (Create).

---

#### [BLOCKER 3] Reconcile Rate Limiting Directive
* **Affected Scope**: Items 16 & 17 vs Line 43
* **Details**:
  - If rate limiting is kept as-is (20 req/min per IP), delete Items 16 & 17, note the 100-user NAT limit trade-off, and keep the existing code untouched.
  - If rate limiting is partitioned, update the Open Questions answer to confirm partitioned rate limiting (100 req/min on `/api/v1/auth/refresh`).

---

### 🟡 Major Gaps

#### [MAJOR 1] Exact Dependency Injection and Entity Sequences in Command Handlers
* **Affected Scope**: Items 7 & 9 (lines 312–325)
* **Details**:
  - In `LoginCommandHandler`: Inject `IRoleRepository roles`. Call `users.FindByIdWithPermissionsAsync(user.Id, ct)` to load roles and overrides, call `roles.FindByIdsAsync(...)`, compute bitmask via `PermissionBitmaskMapper.ToBitmask(...)`, and pass to `CreateAccessToken`.
  - In `RefreshTokenCommandHandler`: Inject `IUserRepository users` and `IRoleRepository roles`. Extract `claims.UserId` from `tokens.TryValidateRefreshToken`. Call `sessions.FindByIdForUpdateAsync(claims.SessionId, ct)` and `session.TryRotate(...)`. Update session with synchronous `_sessionRepository.Update(session)`. Let MediatR `TransactionBehavior` commit the transaction.

#### [MAJOR 2] `PermissionBitmaskMapper` Unknown Permission Semantics
* **Affected Scope**: Item 2 (lines 284–291)
* **Details**: Explicitly specify:
  - `GetBit(string permissionName)` returns `PermissionBits.None` if the permission string is unknown or unrecognized, causing authorization to safely fail closed.
  - `ToBitmask(IEnumerable<string> permissionNames)` ignores unknown strings.
  - Support alias mapping for `user:manage` to `PermissionBits.UserWrite | PermissionBits.RoleManage` if required by controller attributes.

---

### 🟢 Nit Gaps

* **[NIT 1] Imperative Action Phrasing**: Rename `Rationale` to `Action` across all items, using imperative verbs (`Define`, `Implement`, `Set`, `Update`, `Verify`).
* **[NIT 2] Consolidate README Tasks**: Fold documentation tasks (Items 4, 6, 8, 10, 12, 15, 17) directly into their respective code modification tasks to streamline execution tracking.
