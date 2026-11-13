# Quickstart Validation Guide: Token Bitmask Authorization

**Feature Branch**: `002-token-bitmask-authorization`  
**Date**: 2026-11-12  
**Status**: Completed  

---

## 1. Prerequisites & Environment Setup

- **SDK**: .NET 10.0 SDK (`dotnet --version` >= 10.0.100)
- **Database**: PostgreSQL test instance (or Testcontainers harness configured in integration tests)
- **Solution Root**: `/mnt/projects/web/backend/dotnet/CsRaiders.Api`

### Setup Command
```bash
dotnet build --no-restore
```

---

## 2. Automated Test Verification

Execute the test suites corresponding to each layer of the feature:

### 2.1 Domain Bitmask Unit Tests
Validates bidirectional translation, fail-closed handling for unknown strings, high-bit boundaries, and flags evaluation:
```bash
dotnet test --no-restore --filter "FullyQualifiedName~CsRaiders.Api.Domain.UnitTests.PermissionBitmaskMapperTests"
```
*Expected Outcome*: All test cases pass. Zero bitmask precision loss across all 20 permissions.

### 2.2 Application Handlers Unit Tests
Validates login bitmask resolution, refresh token fail-fast sequencing, session rotation, and memory cache hit behavior:
```bash
dotnet test --no-restore --filter "FullyQualifiedName~CsRaiders.Api.Application.UnitTests.Auth"
```
*Expected Outcome*: 
- Invalid/replayed refresh tokens reject before executing database queries.
- Cache hit on refresh reuses bitmask without querying `users` or `roles`.

### 2.3 Integration Authorization Tests
Validates end-to-end HTTP pipeline authorization with bitmask claims:
```bash
dotnet test --no-restore --filter "FullyQualifiedName~CsRaiders.Api.IntegrationTests.PermissionAuthorizationHandlerTests"
```
*Expected Outcome*: 
- Access token with matching bit succeeds.
- Access token lacking bit receives HTTP 403.
- Access token missing `"perms"` or with invalid format receives immediate HTTP 403 with zero database fallback.

---

## 3. End-to-End Manual / API Validation Scenarios

Refer to [contracts/auth-refresh-api.md](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/specs/002-token-bitmask-authorization/contracts/auth-refresh-api.md) and [contracts/permission-authorization-policy.md](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/specs/002-token-bitmask-authorization/contracts/permission-authorization-policy.md) for complete payload schemas.

### Scenario 1: Issue 5-Minute Access Token with Bitmask
1. Send `POST /api/v1/auth/login` with valid user credentials.
2. Decode the returned `accessToken` JWT payload using a JWT inspector.
3. **Verify**:
   - The token contains `"perms"` as a base-10 decimal string (e.g. `"1048575"`).
   - The token expiration `exp` is exactly 5 minutes (300 seconds) after `iat`.

### Scenario 2: Successful Request to Protected Endpoint
1. Make a request to an endpoint protected by `[HasPermission("course:read")]` (e.g. `GET /api/v1/courses`).
2. Attach header `Authorization: Bearer <valid_token>`.
3. **Verify**:
   - Response status is `200 OK`.
   - Authorization executes purely via in-memory bitwise operation with zero database queries.

### Scenario 3: Denial When Permission Bit Is Missing
1. Issue a token for a user with only `CourseRead` (bitmask `"1"`).
2. Make a request to an endpoint protected by `[HasPermission("course:write")]` (bit index 1, value `2UL`).
3. Attach header `Authorization: Bearer <token>`.
4. **Verify**:
   - Response status is `403 Forbidden`.
   - No database lookup is executed.

### Scenario 4: Fail-Fast Rejection on Missing or Corrupt Bitmask Claim
1. Craft an access token signed with the test key that omits the `"perms"` claim (or has `"perms": "-5"` or `"perms": "abc"`).
2. Call a protected endpoint with this token.
3. **Verify**:
   - Response status is `403 Forbidden`.
   - The request fails immediately without database or cache fallback.

### Scenario 5: Concurrency-Safe Refresh & Memoization
1. Call `POST /api/v1/auth/refresh` using the active refresh token cookie.
2. Verify response is `200 OK` with a new access token and rotated refresh token cookie.
3. Immediately call `POST /api/v1/auth/refresh` again with the same old refresh token.
4. **Verify**:
   - The second request is rejected with `401 Unauthorized` (`session_compromised`).
   - A `token_reuse_detected` security audit event is recorded.
   - Zero permission database queries are executed for the compromised attempt.

### Scenario 6: Immediate Containment of Sensitive Capability Revocation
1. Identify a user with active sessions holding `role:manage` (`1UL << 9`).
2. As an administrator, execute `UpdateRolePermissions` or `SetUserPermissionOverride` removing `role:manage`.
3. Within 5 seconds, attempt an authenticated request using the user's previously issued access token.
4. **Verify**:
   - `SessionValidationMiddleware` intercepts the request and returns `401 Unauthorized` (`session_superseded`).
   - Access is blocked without waiting for the 5-minute access token lifetime to expire.

### Scenario 7: Rate Limit Partitioning on Shared NAT
1. Issue 25 requests within 1 minute from the same IP address to `POST /api/v1/auth/refresh`.
   - **Verify**: All 25 requests succeed (under the 100 req/min partition limit).
2. Issue 25 requests within 1 minute from the same IP address to `POST /api/v1/auth/login`.
   - **Verify**: The 21st request returns `429 Too Many Requests` (under the 20 req/min auth limit).
