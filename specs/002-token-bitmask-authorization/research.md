# Phase 0 Research: Token Bitmask Authorization

**Feature Branch**: `002-token-bitmask-authorization`  
**Date**: 2026-11-12  
**Status**: Completed  

---

## 1. 64-Bit Capability Representation & Claim Serialization

### Decision
Represent user effective permissions as an unsigned 64-bit integer (`ulong` / `UInt64`) backed by a domain flags enum (`PermissionBits`), and serialize it into JWT access tokens as a base-10 decimal integer string under the claim name `"perms"`.

### Rationale
- **IEEE-754 Precision Safety**: Standard JavaScript JSON decoders (`JSON.parse()`) parse unquoted numbers as 64-bit floating-point values. Floating-point numbers lose integer precision beyond $2^{53} - 1$ (`Number.MAX_SAFE_INTEGER`). Encoding the 64-bit bitmask as a decimal string prevents bit truncation for permission positions 54 through 63 in web clients.
- **Client Usability**: Web and mobile clients can easily decode decimal strings into arbitrary-precision integers using native JavaScript `BigInt(jwtPayload.perms)` and evaluate permissions via standard bitwise operators: `(BigInt(perms) & (1n << BigInt(bitIndex))) !== 0n`.
- **Zero-Allocation Parsing in .NET**: In ASP.NET Core, `ulong.TryParse(permsClaim.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bitmask)` executes with zero heap allocations and sub-microsecond latency.
- **Unsigned Domain Modeling**: Using `ulong` ensures all 64 bits represent valid positive flags, avoiding sign-bit interpretation complexities or negative two's-complement strings.
- **Compact Token Size**: The decimal representation of all 20 currently defined permissions (`1048575`) is only 7 ASCII bytes. Even with all 64 bits set (`18446744073709551615`), the claim is 20 ASCII bytes, keeping the total JWT well under 280 bytes and avoiding reverse proxy header limits (`HTTP 431 Request Header Fields Too Large`).

### Alternatives Considered
- **Unquoted JSON Numeric Scalar (`"perms": 1048575`)**: Rejected because bits above $2^{53} - 1$ would be silently corrupted when parsed by standard JavaScript frontend clients.
- **Hexadecimal String (`"perms": "0x000FFFFF"`)**: Rejected because base-10 integer string is standard, slightly shorter for small bitmasks, and parses directly with `ulong.TryParse(..., NumberStyles.None)`.
- **Base64-Encoded Byte Array**: Rejected because decimal string is directly human-readable in debug tools (e.g. jwt.io) and native `BigInt` can parse decimal strings directly without an intermediate Base64 decode step.
- **Raw Array of Permission Strings (`"perms": ["course:read", ...]`)**: Rejected due to token payload bloat (> 1.5 KB with 20 permissions), increased network overhead, and potential edge-proxy rejections.

---

## 2. In-Memory Authorization Handler Architecture

### Decision
Refactor `PermissionAuthorizationHandler` to perform pure synchronous in-memory bitwise evaluation (`(bitmask & requiredBit) != 0UL`) with zero I/O, removing all dependencies on `IUserRepository`, `IRoleRepository`, `IMemoryCache`, `IRbacInvalidationToken`, and `IAuthMetricsRecorder`. Fail immediately without dual-read fallback if `"perms"` is missing, malformed, or unauthorized.

### Rationale
- **Sub-Microsecond Latency**: The authorization evaluation executes entirely on CPU in under 50 nanoseconds per request with zero heap allocations.
- **Elimination of Database & Cache Contention**: Removes the `perms:{userId}` cache key formatting, cache synchronization locks, and PostgreSQL split queries from the request path of all permission-protected endpoints.
- **Clean Architecture & Decoupling**: Endpoint authorization becomes purely a token-validation concern; database access is strictly isolated to authentication and token issuance workflows.
- **Fail-Fast Security Posture**: In accordance with FR-014 and the pre-production state of the application, tokens lacking `"perms"` fail immediately. Dual-read fallback is eliminated to avoid dead code, attack surface, and cache thrashing.

### Alternatives Considered
- **Dual-Read Fallback to Database/Cache**: Rejected because CsRaiders.Api is pre-production with no live legacy clients. Omitting fallback guarantees that all clients migrate immediately and removes unnecessary fallback code paths.
- **Caching Parsed Bitmask in `HttpContext.Items`**: Rejected because `ulong.TryParse` takes ~5 ns; caching in `HttpContext.Items` would introduce dictionary lookups that are slower than parsing the short decimal string directly.

---

## 3. Fail-Fast Token Refresh Pipeline & Concurrency Safety

### Decision
Enforce a strict fail-fast ordering in `RefreshTokenCommandHandler`:
1. Cryptographically validate refresh token signature and expiration via `ITokenService.TryValidateRefreshToken`.
2. Acquire pessimistic session row lock via `ISessionRepository.FindByIdForUpdateAsync(claims.SessionId, ct)`.
3. Verify session ownership and execute `session.TryRotate(presentedHash, nextHash, fingerprint, now)`.
4. If rotation fails (`DeviceMismatch`, `ReuseDetected`, `Inactive`), return failure immediately without executing any permission queries.
5. On `RefreshOutcome.Rotated`, retrieve the user bitmask from `IMemoryCache` (miss: resolve from PostgreSQL via `users` + `roles` and cache for 10 minutes), issue the replacement 5-minute access token, and commit the transaction.

### Rationale
- **Prevention of Unauthenticated DoS Amplification**: With access token lifetime reduced to 5 minutes, token refresh traffic triples. Executing permission joins before verifying the cryptographic presentation, expiration, or rotation validity of the refresh token would create an unauthenticated query amplification attack against PostgreSQL.
- **Race Condition Prevention**: The pessimistic row lock (`SELECT ... FOR UPDATE`) guarantees sequential evaluation during concurrent refresh attempts, safely detecting token replay or device mismatch before committing token rotation.
- **Database Shielding**: Caching the resolved bitmask in `IMemoryCache` ensures that 99%+ of refresh operations execute zero queries against `users` and `roles`.

### Alternatives Considered
- **Query Permissions Before Session Lock**: Rejected because invalid or replayed refresh tokens would trigger expensive multi-table joins.
- **Unconditional Database Query on Every Refresh**: Rejected because 10,000 active users would generate ~67 queries/second against `users` and `roles`, starving the connection pool and causing lock contention on the database.

---

## 4. Invalidation-Aware In-Memory Bitmask Caching on Refresh

### Decision
Store resolved user bitmasks in `IMemoryCache` under the key `perms_mask:{userId}` with a 10-minute sliding/absolute expiration, linked to `CancellationChangeToken(invalidation.CurrentToken)`.

### Rationale
- **Immediate Invalidation on Administrative Changes**: When an administrator modifies a role or user permission override via `RbacCommands`, `invalidation.Invalidate()` cancels the change token, instantly evicting all cached bitmasks across the application instance.
- **High Cache Hit Ratio**: With a 5-minute token lifetime and 10-minute cache TTL, active users refreshing every 4.5 minutes will consistently hit the cache, keeping database load under 1% of refresh traffic.

### Alternatives Considered
- **Redis Distributed Cache**: Unnecessary complexity for single-node / containerized service instances at this scale; `IMemoryCache` provides sub-microsecond access with zero network hops.
- **Persisting Bitmask in PostgreSQL `users` Table**: Rejected by FR-008. Preserving string-based permission records avoids database schema migrations, prevents enum drift corruption, and retains granular audit fields (`granted_by`, `granted_at`).

---

## 5. Propagation Window vs. Sensitive Capability De-escalation

### Decision
Bound standard permission changes to a 5-minute propagation window (plus 30 seconds clock skew tolerance). For designated sensitive permissions (`role:manage`, `payment:refund`, `user:write`, `session:revoke`), require administrative commands in `RbacCommands.cs` to call `ISessionRepository.RevokeAllByUserIdAsync(targetUserId, ...)` and evict session cache, terminating active sessions within 5 seconds via `SessionValidationMiddleware.cs`.

### Rationale
- **Balanced Operational Impact**: Ordinary permission additions/removals (e.g. course editing, lesson access) do not abruptly terminate active user sessions; changes take effect naturally at the next 5-minute refresh.
- **Immediate High-Risk Containment**: Critical administrative de-escalations (e.g. removing role management or refund capabilities) cannot wait 5 minutes. Revoking active sessions ensures access is blocked on the subsequent request within the 5-second session cache window.

### Alternatives Considered
- **Instant Revocation for All Permissions**: Rejected because revoking sessions on any permission change causes disruptive logouts for legitimate users during minor role adjustments.
- **Global Token Blacklist on Every Request**: Rejected because checking a distributed blacklist on every API request reintroduces cache/database latency on protected endpoints, defeating the purpose of token-embedded authorization.

---

## 6. Shared NAT Resilience & Rate Limiting Partitioning

### Decision
In `CsRaidersApiServiceExtensions.cs`, configure `PartitionedRateLimiter` with distinct partition keys:
- `$"refresh_{ip}"` for `POST /api/v1/auth/refresh` with a limit of 100 requests per minute.
- `$"auth_{ip}"` for all other `/api/v1/auth` routes with a limit of 20 requests per minute.

### Rationale
- **NAT Multiplexing**: Multiple users behind a corporate, campus, or mobile NAT gateway share a single public IP. With 5-minute access tokens, background refreshes from multiple users would easily exceed 20 requests per minute, causing cascading HTTP 429 lockouts.
- **Targeted Protection**: Strict rate limiting (20 req/min) is preserved for brute-force vulnerable endpoints (`/login`, `/register`, `/verify-otp`), while legitimate refresh traffic from shared IPs is accommodated up to 100 req/min.

### Alternatives Considered
- **Increase All Auth Routes to 100 req/min**: Rejected because it weakens defense against credential stuffing and brute force on login routes.
- **Rate Limit by Refresh Token / Session ID**: Rejected because unauthenticated or corrupted requests lack valid session identifiers, requiring an IP-level backstop to prevent socket exhaustion.

---

## 7. Capability Bitmask Mapping Structure

### Decision
Define `PermissionBits` as a `[Flags]` enum backed by `ulong` in `CsRaiders.Api.Domain.Constants`, assigning static powers of two (`1UL << 0` to `1UL << 19`) to all 20 permissions in `PermissionCatalog.cs`. Provide `PermissionBitmaskMapper` with static translation methods:
- `ulong ToBitmask(IEnumerable<string> permissionNames)`
- `ulong GetBit(string permissionName)`
- `bool HasPermission(ulong bitmask, ulong requiredBit)`
- `IReadOnlyList<string> ToPermissionNames(ulong bitmask)`

### Rationale
- **Compile-Time Static Mapping**: Eliminates runtime dictionary allocations or synchronization.
- **Fail-Closed Semantics**: Unrecognized or null permission strings return `0UL` and are safely skipped during bitwise OR operations.
- **Domain Purity**: Lives inside `CsRaiders.Api.Domain` without external dependencies.
