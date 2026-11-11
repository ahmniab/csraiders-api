# Stage 6 Comprehension Test Report

**Target Document:** [`docs/designs/token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md)  
**Feature Range:** lines 1..271  
**Detailed Range:** lines 272..360  
**Evaluator:** Naive Reader Comprehension Auditor (`egm-comprehension`)

---

## 1. Paraphrase of Feature Specification (FeatureRange: lines 1..271)

The feature specification addresses query and cache overhead during endpoint authorization checks by embedding user permissions as a 32-bit scalar unsigned integer bitmask (`"perms"`) directly inside access tokens. 

### Core Mechanics Described
* **Bitwise Evaluation**: Endpoints decorated with `[HasPermission]` evaluate access in < 1 nanosecond via bitwise AND against the `"perms"` claim without querying `IMemoryCache` or PostgreSQL.
* **Bounded Permission Propagation**: Access token lifetime is reduced from 15 minutes to 5 minutes so administrative permission updates propagate within 5 minutes across background client refreshes, while user login sessions remain long-lived (governed by refresh token validity).
* **Dual-Read Migration Fallback**: To prevent HTTP 403 errors on in-flight 15-minute legacy tokens lacking the `"perms"` claim during rolling deployment, `PermissionAuthorizationHandler` falls back to the existing in-memory cache and database path when `"perms"` is absent.
* **Refresh Concurrency Optimization**: To avoid database connection pool exhaustion under 3x refresh volume, `RefreshTokenCommandHandler` resolves permissions prior to acquiring an exclusive pessimistic row lock on `sessions`, holding the lock for < 2 milliseconds to rotate the refresh token hash.
* **NAT Rate Limiting**: `/api/v1/auth/refresh` is partitioned into a dedicated 100 req/min rate limit to prevent false-positive HTTP 429 lockouts for multiple users sharing an egress IP.
* **Security & Invalidation**: Session revocation (logout, bans) remains instant (< 5 seconds) via `SessionValidationMiddleware.cs`.

---

## 2. Paraphrase of Detailed Implementation Plan (DetailedRange: lines 272..360)

The implementation plan details 19 file changes across all architecture layers:
1. **Domain Layer**:
   * Create `PermissionBits.cs` (32-bit flags enum mapping 20 permissions).
   * Create `PermissionBitmaskMapper.cs` (static utility for bitmask conversion and evaluation).
   * Modify `AuthRules.cs` (set `AccessTokenLifetime = TimeSpan.FromMinutes(5)`).
   * Update `src/CsRaiders.Api.Domain/Constants/README.md`.
2. **Application Layer**:
   * Modify `ITokenService.cs` (add `uint permissionsBitmask` parameter to `CreateAccessToken`).
   * Update `src/CsRaiders.Api.Application/Common/Interfaces/Security/README.md`.
   * Modify `LoginCommand.cs` (resolve permissions, convert to bitmask, pass to `CreateAccessToken`).
   * Update `src/CsRaiders.Api.Application/Features/Auth/Commands/Login/README.md`.
   * Modify `RefreshTokenCommand.cs` (decouple permission resolution from `sessions` row lock, issue bitmask token).
   * Update `src/CsRaiders.Api.Application/Features/Auth/Commands/Refresh/README.md`.
3. **Infrastructure Layer**:
   * Modify `TokenService.cs` (serialize `"perms"` as decimal integer string claim in JWT).
   * Update `src/CsRaiders.Api.Infrastructure/Security/README.md`.
4. **API Layer**:
   * Modify `PermissionAuthorizationHandler.cs` (bitwise check on `"perms"` claim with fallback to `IMemoryCache`).
   * Modify `HasPermissionAttribute.cs` docstring.
   * Update `src/CsRaiders.Api.API/Authorization/README.md`.
   * Modify `CsRaidersApiServiceExtensions.cs` (partition rate limiting for `/api/v1/auth/refresh`).
   * Update `src/CsRaiders.Api.API/Extensions/README.md`.
5. **Testing Layer**:
   * Create `PermissionBitmaskMapperTests.cs` (mapping, flag combination, and lookup tests).
   * Modify `PermissionAuthorizationHandlerTests.cs` (fast bitwise path and dual-read fallback test cases).

---

## 3. Comprehension Findings & Ambiguities

### 🟡 Finding 1: Refresh Token Hash Resolution Inconsistency & Query Amplification (Major)
* **Affected Scope**: Section 5 (lines 190–198) and Target File 9 (lines 318–320).
* **Details**: Line 191 states: *"Extract `sessionId` from the refresh token hash or load the session without pessimistic locking."*
  1. A cryptographic hash is a one-way digest; a GUID cannot be extracted from a hash.
  2. The text leaves an ambiguous "either/or" choice rather than providing a concrete implementation specification.
  3. Running permission resolution queries *before* validating the refresh token hash and session expiration risks executing expensive database queries on unauthenticated, expired, or replayed tokens.
* **Remediation**: Clarify that session lookup and cryptographic token validation occur first, with permissions retrieved from an in-memory cache or resolved prior to acquiring the pessimistic row lock.

---

### 🟡 Finding 2: Open Questions Inconsistency with Document Body (Major)
* **Affected Scope**: "Open Questions" (lines 38–42), Section 3 (lines 147–153), Section 6 (lines 205–211), Target Files 11 & 16.
* **Details**:
  1. Open Question 1 asks if `"perms"` should be decimal or hexadecimal, yet the body already standardizes on decimal string serialization (`permissionsBitmask.ToString(CultureInfo.InvariantCulture)`).
  2. Open Question 2 asks whether rate limiting should be partitioned by IP (100 req/min) or by session/user, while Section 6 mixes both and Section 16 prescribes 100 req/min per IP.
* **Remediation**: Resolve Open Question 1 as decimal, and commit Section 6 / Section 16 to a clear rate limiting policy (session-keyed partitioning).

---

### 🟡 Finding 3: Unspecified Error Contract for Unrecognized Permissions (Major)
* **Affected Scope**: Section 2 (lines 136–137), Section 4 (lines 171–172), Target File 2 (lines 287–288).
* **Details**: `PermissionBitmaskMapper.GetBit(requirement.Permission)` is invoked in the hot path. If an unmapped or misspelled permission string is supplied, the behavior is undefined: does it return `PermissionBits.None` (0), throw an exception, or return null?
* **Remediation**: Explicitly specify that `GetBit` returns `PermissionBits.None` on unmapped permissions, causing authorization to safely fail closed (`false`).

---

### 🟡 Finding 4: Missing Registration Command & Mock Test Updates (Major)
* **Affected Scope**: Target Files 5, 7, 9, 18, 19 (lines 300–361).
* **Details**:
  1. If `/api/v1/auth/register` issues access tokens, `RegisterCommand.cs` must be updated with the new `CreateAccessToken` signature.
  2. Existing unit test suites for `LoginCommandHandlerTests`, `RefreshTokenCommandHandlerTests`, and `TokenServiceTests` that mock `ITokenService` will break compilation unless updated.
* **Remediation**: Verify whether `RegisterCommand` issues tokens, and add the existing command handler unit test files to the target modification list.

---

### 🟢 Finding 5: Malformed `perms` Claim Fallback Semantics (Nit)
* **Affected Scope**: Section 4 (lines 168–185).
* **Details**: If `"perms"` claim exists but fails `uint.TryParse` (e.g. malformed value), does the handler fail authorization immediately or fall back to `IMemoryCache`?
* **Remediation**: Specify that a malformed claim immediately fails authorization (`false`), avoiding fallback mask attacks.

---

### 🟢 Finding 6: Unexplained Acronyms & Undefined Interfaces (Nit)
* **Affected Scope**: "The Problem" (line 21), CUJ 2 (line 51), Alternatives (line 224), NFRs (line 245).
* **Details**: `sub` (Subject / UserId) and `sid` (SessionId) are referenced without definition. `IRbacInvalidationToken` and `ISessionStatusReader` are referenced without defining their packages or roles.
* **Remediation**: Define `sub` and `sid` explicitly in CUJs, and clarify the roles of `IRbacInvalidationToken` and `ISessionStatusReader`.

---

### 🟢 Finding 7: Helper Method Redundancy in Handler Snippet (Nit)
* **Affected Scope**: Section 2 (line 137), Section 4 (line 172), Target File 2 (line 288).
* **Details**: `PermissionBitmaskMapper.HasPermission(uint, PermissionBits)` is defined in Section 2, but the Section 4 handler snippet bypasses it with manual bitwise syntax `(bitmask & (uint)requiredBit) != 0`.
* **Remediation**: Use `PermissionBitmaskMapper.HasPermission` consistently in the handler snippet.
