# CS Raiders — Authentication & Session Management (Revision 2)

Author: ahmniab · Status: Draft · Revised: 2026-11-08 (supersedes the 2026-11-07 draft)

## Revision Notes

This revision closes both open questions and folds in the review findings. The main changes:

- **Session cache** uses absolute expiry (5s) plus explicit eviction on every server-side revoke. The old sliding expiry let a busy session skip revalidation forever.
- **Login concurrency** is serialized per user with a PostgreSQL advisory lock, covering both the single-session and multi-session cap paths.
- **Refresh rotation** gains a 15-second grace window on the previous token, so retries and multi-tab refreshes are not treated as theft.
- **Lockout** is tracked per (email, IP) pair and returns a uniform 401. HTTP 423 is removed. Per-email limiting moves from the rate limiter into the handler.
- **CSRF** is simplified: SameSite=Strict refresh cookie, Origin validation, and a required custom header. The CSRF cookie is removed.
- **Registration** gets resend, cooldowns, and a rule that re-registering a pending email never overwrites the stored password.
- **Email delivery** uses a bounded in-memory queue with a background worker. No outbox. Users resend on failure.
- **New items:** account suspension, change password, cleanup job, pepper versioning, JWT hardening, global RBAC cache invalidation, corrected audit redaction list, and extra tests.

## Overview

This document specifies authentication, session management, and permission-based RBAC for the CS Raiders ASP.NET Core monolith. PostgreSQL 16 is the single source of truth for sessions, lockout state, and replay detection. An in-process IMemoryCache in SessionValidationMiddleware keeps database reads off the request hot path. There is no Redis, no message broker, and no Kubernetes. The app runs as one process behind one nginx reverse proxy.

## The Problem

1. **Account sharing.** Students get one active session. A login from a second device supersedes the first immediately. Admins and instructors with `session:multi` get a capped number of sessions.
2. **Credential and token attacks.** Credential stuffing, timing-based user enumeration, OTP brute force, refresh token replay, and lockout abuse must all be handled.
3. **Database load vs. revocation speed.** Querying PostgreSQL on every request saturates the connection pool. Trusting 15-minute JWTs alone leaves revoked sessions alive too long. A short absolute cache plus explicit eviction resolves this.
4. **Fine-grained RBAC.** Authorization is permission-based (`[HasPermission]`) with per-user GRANT and DENY overrides, and DENY wins.

## Goals

- Argon2id (m=19456 KiB, t=2, p=1) with a versioned server-side pepper, and a bounded semaphore with a wait timeout.
- Uniform responses and timing for register, resend, login, and password reset regardless of whether the email exists.
- OTP registration: 6-digit CSPRNG code, HMAC-SHA256 bound to the email, 5 attempts, 10-minute expiry.
- Durable two-tier brute-force defense: IP rate limiting in middleware, per-(email, IP) progressive delay and 15-minute block in PostgreSQL.
- Single active session per user by default, multi-session capped for `session:multi`.
- 15-minute access JWT plus 30-day rotating refresh token in a `__Host-` HttpOnly cookie, with replay detection.
- Revocation visible within 5 seconds in the worst case and immediately in the common case (explicit cache eviction).
- Effective permissions = (Roles union Grants) minus Denies, cached 30s and invalidated globally on any RBAC change.

## Non-Goals

- External caches or brokers (Redis, KeyDB, Memcached, RabbitMQ) for Milestone 0.
- Social login (OAuth2/OIDC).
- Passwordless (WebAuthn, passkeys).
- Roles or permissions inside JWT claims.
- Multi-instance deployment. The in-process caches assume one instance (see Scaling Note).

## Resolved Open Questions

### Reverse proxy and client IP

One nginx on the same host terminates TLS and proxies to Kestrel over HTTP.

- `app.UseForwardedHeaders()` is the first middleware, before logging, rate limiting, and auth.
- `ForwardedHeaders = XForwardedFor | XForwardedProto`, `ForwardLimit = 1`.
- Loopback is trusted by default. If nginx runs elsewhere (for example a Docker bridge), its address goes in `Proxy:KnownProxies` config.
- nginx **overwrites** the header and never appends: `proxy_set_header X-Forwarded-For $remote_addr;` and `proxy_set_header X-Forwarded-Proto $scheme;`.
- Kestrel binds to `127.0.0.1` (or a private network) only, so the proxy cannot be bypassed.
- HSTS is set at nginx.
- A test sends a spoofed `X-Forwarded-For` from an untrusted source and asserts it is ignored.

### Local HTTPS

- Local development runs Kestrel with HTTPS using an mkcert certificate (more reliable on Linux than `dotnet dev-certs https --trust`). Optionally run local nginx with the same certificate to mirror production.
- `WebApplicationFactory` clients use `BaseAddress = new Uri("https://localhost")` so Secure cookies round-trip.
- The SPA is served at `/` and the API under `/api` on a single host. No CORS is needed.

## Critical User Journeys

**CUJ 1: Registration and verification.** A prospective student submits name, email, and password to `POST /api/v1/auth/register`. The password is Argon2id-hashed and stored in `pending_registrations` with an HMAC OTP hash, and a code is emailed through the queue. At `POST /api/v1/auth/verify-otp` the correct code creates the user with the `Student` preset and purges the pending row. If the email never arrives, `POST /api/v1/auth/resend-otp` issues a fresh code.

**CUJ 2: Single-session supersession.** A student logs in on Device B while Device A is active. In one transaction under a per-user advisory lock, Device A's session is revoked (`superseded`) and Device B's session is created. The server evicts Device A's cache entry immediately, so Device A gets `401 session_superseded` on its next request.

**CUJ 3: Refresh theft detection.** An attacker exchanges a stolen refresh token. When the legitimate client later presents the old token **after the grace window**, the server revokes the session (`reuse_detected`), writes a security audit event, clears the cookie, and returns `401 session_compromised`.

**CUJ 4: Live policy change.** A user with `session:multi` and 3 sessions loses the permission. In one transaction the two older sessions are revoked (`policy_reduced`) and the newest becomes single-session. All affected cache entries are evicted.

**CUJ 5: Resend.** A user whose email was lost (queue full, SMTP down, app restart) taps resend. A new OTP replaces the old one, attempts reset, and expiry extends.

## Technical Plan

### 1. Request pipeline

Order is fixed:

1. `UseForwardedHeaders`
2. Serilog request logging (TraceId, redaction)
3. RateLimiter, **IP partitions only**: 20 requests per minute per IP on `/api/v1/auth/*`. Per-email limits live in handlers, because the limiter runs before the body is read.
4. JwtBearer authentication: validates signature, `exp`, issuer, audience. Algorithm pinned to HS256. `ClockSkew = 30s`. Extracts `sub` and `sid`.
5. `SessionValidationMiddleware`
6. `PermissionAuthorizationHandler`
7. Controller or handler
8. `AuditSaveChangesInterceptor` (same transaction)

**SessionValidationMiddleware:**

- Looks up `session:status:{sid}` in IMemoryCache. The cached value holds `expires_at` and `idle_expires_at`, and the middleware checks them on every hit.
- On a miss, runs `SELECT revoked_at, expires_at, idle_expires_at FROM sessions WHERE id = @sid`.
- Revoked or expired: removes the entry, clears cookies, returns `401 session_superseded`.
- Active: caches with **absolute** expiration of 5 seconds. Sliding expiration is not used.
- Every server-side revoke (supersede, cap exceeded, policy reduced, logout, password change or reset, suspension, reuse detected) evicts `session:status:{sid}` after the transaction commits.
- `last_seen_at` writes are debounced to once per 60 seconds per session.

### 2. Cryptography and secrets

- **Password hashing:** input is NFC-normalized, then `pre = HMAC-SHA256(key = Auth:PasswordPepper[version], data = password)`, then Argon2id over `pre` (128-bit salt, 256-bit tag, PHC string). The pepper version is stored per row (`pepper_version`) so it can be rotated, and login rehashes when the version or parameters are outdated.
- **Dummy verification:** a static dummy hash with identical cost, run through the same pepper step, executes when the user is not found.
- **Concurrency guard:** a SemaphoreSlim sized to physical cores with a 2-second wait timeout. On timeout the API returns `503` instead of queueing indefinitely.
- **Registration OTP:** HMAC-SHA256 keyed by `Auth:OtpHmacKey` over `email:otp`, compared with FixedTimeEquals, 5 attempts, 10-minute TTL.
- **Refresh and reset tokens:** 32-byte Base64Url CSPRNG, stored as SHA-256 and compared with FixedTimeEquals. Reset TTL is 30 minutes and tokens are single-use.
- **JWT:** 15-minute lifetime, claims `sub`, `sid`, `iat`, `exp`. No roles or permissions.
- **Startup checks:** pepper and signing keys must meet minimum entropy or the app refuses to start.

### 3. Login and session concurrency

Login runs in one transaction that begins with a per-user advisory lock:

```sql
SELECT pg_advisory_xact_lock(hashtextextended(@user_id::text, 0));
```

With the lock held, there is no race between supersede and insert:

- **Single-session (default):** `UPDATE sessions SET revoked_at = now(), revoke_reason = 'superseded' WHERE user_id = @u AND revoked_at IS NULL`, then insert the new session. The partial unique index `uq_sessions_one_active` stays as a backstop, and the handler retries once on `23505`.
- **Multi-session (`session:multi`):** count active sessions, revoke the oldest while the count is at or above `session.multi_max_concurrent` (default 3) with `cap_exceeded`, then insert.
- After commit, evict the cache entries of every revoked session.

The same advisory lock is taken by SessionReconciliationService and by password reset and suspension flows, so they serialize with logins for the same user.

### 4. Login attempts and lockout

- `login_attempts` is keyed by (email\_normalized, ip) and is written for unknown emails too, so known and unknown emails behave identically.
- Failures 1 to 3 have no delay. Failures 4 to 9 set a `not_before` timestamp (2^(n-3) seconds, capped at 15). Failure 10 sets a 15-minute block.
- While a pair is delayed or blocked, the request is rejected **before** password verification with `401 invalid_credentials` and a `Retry-After` header. A correct password during a block is also rejected.
- Because the key includes the IP, an attacker cannot lock a victim out from other addresses.
- A global per-email soft limit (30 failures in 15 minutes across all IPs) adds delay only and never blocks the real owner.
- Success clears the pair's counters.
- HTTP 423 is not used.

### 5. Refresh rotation and reuse detection

`POST /api/v1/auth/refresh` requires the `__Host-csraiders_rt` cookie and passes the checks in section 6. The handler runs `SELECT ... FOR UPDATE` on the session matching the SHA-256 of the incoming token against `refresh_token_hash` or `previous_refresh_token_hash`.

- **No match:** `401 invalid_grant`, clear cookie.
- **Matches `refresh_token_hash`:** validate `revoked_at`, `expires_at`, `idle_expires_at`; rotate (old hash to `previous_refresh_token_hash`, `rotated_at = now()`, new hash written, `idle_expires_at = now() + 14d`); return a new JWT and cookie.
- **Matches `previous_refresh_token_hash`:**
  - If `now() - rotated_at <= 15s`, treat as a benign retry or concurrent tab: rotate again as above.
  - Otherwise: revoke the session (`reuse_detected`), write the security audit event, clear the cookie, return `401 session_compromised`.
- The reuse revoke **must commit**. The handler returns a result object instead of throwing, so the MediatR transaction behavior does not roll it back.
- Only one previous hash is stored, so a token stolen two rotations ago is not detected. This is an accepted limitation. A token family table is the upgrade path.
- The 15-second window means a thief using a token within 15 seconds of rotation also succeeds. This is an accepted trade-off. The SPA should also use a single-flight lock so only one refresh runs at a time.

### 6. Cookie and CSRF policy

- Refresh cookie: `__Host-csraiders_rt; Secure; HttpOnly; SameSite=Strict; Path=/`.
- There is no CSRF cookie. `/auth/refresh` and `/auth/logout` are the only endpoints that act on a cookie, and they require all of the following:
  - `Origin` equals the configured site origin (falling back to `Referer`; reject if both are missing).
  - An `X-Requested-With: csraiders` header, which cross-site pages cannot add without a CORS preflight.
  - `Content-Type: application/json`.
- Every other endpoint uses the Authorization header, which browsers never attach automatically.
- If stricter protection is wanted later, double-submit can be added without other changes.

### 7. Registration, resend, and email

- `POST /auth/register`: validates input (password 8 to 128 characters, NFC), hashes the password, then branches. Existing user: queue an already-registered notice. Pending registration still valid: **do not overwrite the stored password**; treat the call as a resend request only. No pending row or an expired row: create a fresh one. Always return `202`.
- `POST /auth/resend-otp`: generates a fresh OTP, replaces `otp_hash`, resets `attempts_left` to 5, extends expiry to 10 minutes. Same `202` for unknown, pending, and registered emails.
- Limits per normalized email: 60-second cooldown between sends and a cap of 5 per hour, enforced in the handler.
- **Email queue:** `IEmailQueue` backed by a bounded `Channel<EmailMessage>` (capacity 1000, full means drop and log a warning) and a hosted worker. The worker sends with a 10-second timeout and 3 in-process attempts (1s, 5s, 15s backoff), drains the channel on shutdown, and logs failures with the email redacted and never the OTP or link. Loss on a crash is accepted because the user can resend. There is no outbox.

### 8. RBAC and live reconciliation

- Effective permissions are computed by `EffectivePermissions.Resolve` in `src/CsRaiders.Api.Domain/Policies/EffectivePermissions.cs`.
- `perms:{userId}` is cached for 30s (absolute) and linked to a global CancellationChangeToken. Any role or override mutation cancels the token, which invalidates every entry at once.
- Reconciliation when `session:multi` is lost, in one transaction under the user's advisory lock: revoke older active sessions (`policy_reduced`), then set the newest session to `enforce_single_session = true`. Revoking first keeps `uq_sessions_one_active` satisfied. Evict affected session cache entries afterward.
- Privilege escalation guard (FR-78): an admin cannot grant permissions they do not hold.

### 9. Other flows

- **Suspension:** `users.is_suspended`. Suspending revokes all sessions (`suspended`) and evicts caches. Login for a suspended user returns the same uniform 401.
- **Change password:** requires the current password, updates the hash, and revokes all other sessions (`password_changed`).
- **Forgot and reset password:** uniform `202`, 30-minute single-use token, and a new request invalidates the previous token. Reset revokes all sessions (`password_reset`).
- **Cleanup job:** an hourly BackgroundService deletes sessions revoked or expired more than 30 days ago, expired pending registrations, expired or used reset tokens older than 7 days, and `login_attempts` rows older than 24 hours with no active block.
- **Audit redaction** is by column name: `password_hash`, `otp_hash`, `refresh_token_hash`, `previous_refresh_token_hash`, `token_hash`, plus the request fields `password`, `otp`, `refreshToken`.
- **Audit writer** uses a separate DbContext and connection so events survive rollback of the request transaction.

### Scaling note

The session cache, permission cache, and email queue are per-process. With one instance this is exact. If a second instance is ever added, revocation visibility is still bounded by the 5-second absolute TTL, but explicit eviction and the RBAC change token would only act on the instance that handled the request, so a shared invalidation mechanism (for example PostgreSQL LISTEN/NOTIFY) would be needed first.

## Alternatives Considered

1. **Redis or KeyDB session cache.** Rejected: operational weight is unjustified for Milestone 0 and a single-process cache meets the targets.
2. **Pure stateless JWT.** Rejected: cannot supersede or revoke sessions within the 15-minute token life.
3. **Role-based authorization (`[Authorize(Roles = ...)]`).** Rejected: coarse roles, no per-user GRANT/DENY, and roles would need to live in the JWT, creating stale authorization.
4. **Database query on every request.** Rejected: connection pool saturation under 500 to 1,500 RPS.
5. **PostgreSQL email outbox.** Rejected by author: users can resend, so in-memory delivery is enough for Milestone 0. Revisit if email loss becomes a support problem.
6. **Double-submit CSRF token.** Not adopted: SameSite=Strict, Origin validation, and a custom header cover the two cookie endpoints with less code.

## Non-Functional Requirements

- **Security:** secrets and tokens never appear in logs or audit rows. Comparisons use FixedTimeEquals. HTTPS is mandatory in production.
- **Performance:** cache hits resolve in under 1 ms, P99 under 20 ms for core endpoints, and `last_seen_at` writes are debounced.
- **Rollout:** EF Core migrations with raw SQL for `citext`, `uq_sessions_one_active`, and the `login_attempts` and `rotated_at` additions.
- **Observability:** warnings for queue-full and repeated send failures, and counters for lockouts and `reuse_detected`.

## Implementation Plan

**Application layer (CsRaiders.Api.Application)**

- `IPasswordHasher`, `IOtpGenerator`, `ITokenService`, `ISecurityAuditWriter`, `IEmailQueue` in `Common/Interfaces/Security`.
- Commands and handlers: Register, ResendOtp, VerifyOtp, Login, Refresh, Logout, ForgotPassword, ResetPassword, ChangePassword, SuspendUser.
- Sessions: `GetActiveSessionsQuery`, `RevokeSessionCommands`.
- Admin: `UpdateRolePermissionsCommand` and user override commands.

**Infrastructure layer (CsRaiders.Api.Infrastructure)**

- `Argon2idPasswordHasher` (Konscious, pepper versioning, semaphore with timeout), `HmacOtpGenerator`, `TokenService`.
- `LoginAttemptTracker`, `SessionReconciliationService`, `SecurityAuditWriter`, `ChannelEmailQueue`, `EmailSenderWorker`, `CleanupWorker`.
- `AuditSaveChangesInterceptor` (modify: column-name redaction).

**API layer (CsRaiders.Api.API)**

- `SessionValidationMiddleware`, `HasPermissionAttribute`, `PermissionAuthorizationHandler`.
- `OriginAndHeaderValidationFilter` (replaces CsrfValidationFilter).
- `AuthController`, `MeSessionsController`, `AdminRolesController`, `AdminUsersController`.
- `Program.cs`: forwarded headers first, authentication, rate limiter, cookie policy, startup entropy checks.

**Suggested build order** (one slice per session, tests first, commit per slice): schema and migration; crypto primitives; register, resend, verify-otp; login with attempts and concurrency; refresh with cookies and reuse detection; session middleware and cache; permission handler, RBAC admin, reconciliation; logout, reset, change password, suspension, sessions API, cleanup; architecture tests.

## Verification Plan

**Unit tests:** EffectivePermissionsTests, SessionPolicyTests, Argon2idPasswordHasherTests (PHC format, pepper version, rehash flag), HmacOtpGeneratorTests, validation tests (password length), email queue bounds.

**Integration tests (Testcontainers, https://localhost base address):**

- **SEC-01** No plaintext passwords stored; hashes start with `$argon2id$v=19$`.
- **SEC-02** Unknown and known emails give identical register, resend, and login responses.
- **SEC-03** Five bad OTPs, then a correct OTP is rejected with `400 invalid_or_expired_otp`.
- **SEC-04** Ten failures for a pair block that pair with a uniform 401 and `Retry-After`, survive a host restart, and do not block the same email from a different IP.
- **SEC-05** Ten concurrent logins for one user leave exactly one active session and no unhandled constraint errors.
- **SEC-06** With `session:multi`, four sequential logins revoke the oldest with `cap_exceeded`.
- **SEC-07** Revoking `session:multi` from a user with three sessions yields two `policy_reduced` and one single-session survivor.
- **SEC-08** Replaying a rotated refresh token after the grace window revokes the session (`reuse_detected`).
- **SEC-09** Repeated requests within 5 seconds do not query the sessions table.
- **SEC-10** A session making a request every second is revoked by another device and is rejected on its next request (catches sliding-cache bugs).
- **SEC-11** Replaying the previous token within 15 seconds rotates successfully.
- **SEC-12** Refresh and logout reject a missing or foreign Origin and a missing custom header. Cookie attributes are `Secure; HttpOnly; SameSite=Strict`.
- **SEC-13** JWT validation rejects expired tokens, wrong audience, wrong algorithm, and `alg: none`.
- **SEC-14** A spoofed `X-Forwarded-For` from an untrusted source is ignored.
- **SEC-15** Resend replaces the previous code (the old OTP fails, the new one works).
- **SEC-16** Re-registering a pending email does not change the stored password hash.
- **SEC-17** A suspended user's sessions are revoked and login is rejected with the uniform 401.
- **SEC-18** Changing a role updates effective permissions for all holders within one request.

**Architecture tests:** no `IsInRole` or `[Authorize(Roles = ...)]`; every endpoint is explicitly `[AllowAnonymous]`, `[Authorize]`, or `[HasPermission]`; the middleware cache-hit path does not touch AppDbContext.

The two diagrams from the original draft (request pipeline and refresh flow) still apply. The refresh flow gains the grace-window branch described in section 5.
