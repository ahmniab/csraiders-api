# Phase 0 Research: Authentication & Session Management

Feature: `001-auth-session-management` · Date: 2026-11-08

Every decision below is anchored either to the design documents (`docs/designs/authentication_and_session_management.md` and its **Revision 2**, which supersedes the base) or to the code as it exists today. Where the existing code and Revision 2 disagree, the delta is listed under "Schema/code deltas" at the end. All items that could have been NEEDS CLARIFICATION are resolved here; none remain open.

---

## D1 — Password hashing primitive and pepper versioning

**Decision**: Argon2id (m=19456 KiB, t=2, p=1, 128-bit random salt, 256-bit tag, PHC string output) via `Konscious.Security.Cryptography.Argon2id`. Input is Unicode-NFC normalized, then pre-hashed with `HMAC-SHA256(key = pepper[version], password)`; Argon2id runs over the pre-hash. Hash output becomes `$argon2id$v=19$m=19456,t=2,p=1$<salt-b64>$<tag-b64>$p=<pepperVersion>` (custom PHC extension) so the version is self-describing per row; the existing `PasswordHashGuard` prefix check (`$argon2id$v=19$`) still passes. Concurrency bounded by a `SemaphoreSlim` sized to core count with a **2 s wait timeout** → transparent `503` instead of indefinite queueing. A static dummy hash with identical cost parameters is verified when the email is unknown, so known/unknown paths cost the same work.

**Rationale**: OWASP-native parameters; memory-hardening defeats GPU attacks; the pepper means a database leak alone cannot verify guesses; versioning enables rotation without rehash storms. Design Revision 2 §2 explicitly prescribes this.
**Alternatives considered**: (a) BCrypt/PBKDF2 — not memory-hard; rejected by design. (b) `Isopoh.Cryptography.Argon2id` — alternative impl, no advantage. (c) ASP.NET Identity's `PasswordHasher` — PBKDF2, incompatible with the existing `PasswordHashGuard` contract.

## D2 — Access token design and JWT hardening

**Decision**: 15-minute JWT, HS256 pinned (algorithm allow-list), one symmetric signing key (`Auth:JwtSigningKey`). Claims: `sub`, `sid`, `iat`, `exp` — **no roles/permissions claims**. Validation: issuer, audience, `ClockSkew = 30s`. Startup refuses to run if the signing key or pepper entropy is below minimum.

**Rationale**: Design Revision 2 §2 and §1(4); pinned algorithm blocks `alg: none`/cross-key confusion attacks (SEC-13). `sid` links every token to a revocable server-side session.
**Alternatives considered**: RS256 — unnecessary for a single issuer/verifier with no third parties; asymmetric does not help since permissions are deliberately not in tokens.

## D3 — Session validation cache strategy (supersedes base design)

**Decision**: `IMemoryCache` under `session:status:{sid}` with **absolute expiry of 5 s (not sliding)**; cached value carries `expires_at` and `idle_expires_at` and is re-checked **on every hit**. Cache hit path touches no `DbContext`. Every server-side revoke (supersede, cap exceeded, policy reduced, logout, password change/reset, suspension, reuse detected) explicitly evicts the entry **after** its transaction commits. `last_seen_at` writes debounced to once / 60 s per session (already implemented as `Session.TouchIfDue`).

**Rationale**: Revision 2's revision note — sliding expiry allowed a continuously active session to skip revalidation forever (SEC-10 exists to catch exactly that); absolute + explicit eviction gives ~immediate enforcement in the common case and a hard 5 s ceiling. Base design's sliding 5–10 s is superseded.
**Alternatives considered**: Redis/KeyDB — rejected (Milestone 0 operational weight; single instance). DB on every request — rejected (pool saturation at 500–1,500 RPS).

## D4 — Login concurrency and reconciliation serialization

**Decision**: every login, password reset, suspension, and RBAC reconciliation transaction starts with `SELECT pg_advisory_xact_lock(hashtextextended(user_id::text, 0))` (per-user advisory lock). Supersede-then-insert under the partial unique index `uq_sessions_one_active`; the handler retries once on `23505` as a backstop. `SessionPolicy.Decide` (already implemented) drives single-session (`RevokeAll`) vs. multi-session (`RevokeOldest` after overflow vs. cap `session.multi_max_concurrent`, default 3 — from `app_settings`, pluggable via a settings accessor). Post-commit, evict cache entries for every revoked session.

**Rationale**: Revision 2 §3 — closes the race that the base design solved with an index alone; one serialization point eliminates `23505` risk in the common case.

## D5 — Refresh rotation, grace window, and theft handling

**Decision**: domain logic already exists — `Session.TryRotate` CRCF ordering. Handler flow: `SELECT ... FOR UPDATE` on the session matched by SHA-256 hash against `refresh_token_hash` OR `previous_refresh_token_hash`. Previous-hash match with `now - rotated_at <= 15 s` → benign retry, rotate again. Previous-hash match later → `reuse_detected` revoke, security event, cookie clear, `401 session_compromised` — and the handler **returns a result object rather than throwing** so the MediatR transaction behavior never rolls the revocation back (the revocation is the point; the entity doc comment says the same). New `idle_expires_at = now + 14 d`. Only one previous hash is stored (accepted limitation; token-family table is the upgrade path).

**Rationale**: Revision 2 §5 + §"revision notes"; prevents multi-tab/retry flapping while still catching replay.

## D6 — Device fingerprint binding

**Decision**: the API issues an opaque first-party cookie (`csraiders_dfp`; 1 year, Secure, HttpOnly, SameSite=Strict, not `__Host-` prefixed because it may pre-date first TLS context in tooling) whose random value is stored as `sessions.device_fp` (column already exists) and required verbatim at refresh. Missing/changed value → `RefreshOutcome.DeviceMismatch` → `401 session_superseded` semantics with cookie clear. Registration/login set it; listing sessions shows no fingerprint payload.

**Rationale**: `Session.TryRotate` already enforces `DeviceFp` equality; without a delivery mechanism the value cannot exist. Ties refresh use to the issuing browser while remaining HttpOnly (readable only server-side).

## D7 — Cookie & CSRF posture (supersedes base design)

**Decision**: single `__Host-csraiders_rt` cookie (`Secure; HttpOnly; SameSite=Strict; Path=/`). No CSRF cookie. `/auth/refresh` and `/auth/logout` (the only cookie-acting endpoints) must present **all** of: `Origin` equal to the configured site origin (fall back to `Referer`; reject if both absent), `X-Requested-With: csraiders`, and `Content-Type: application/json`. Every other endpoint is Authorization-header only. Double-submit CSRF was deliberately not adopted (design, Alternatives Considered #6).

## D8 — Durable brute-force state keyed per (email, IP) (revision delta)

**Decision**: `LoginAttempt` domain logic is already correct and durable (`Evaluate` before Argon2 work, exponential delay 2^(n−3) capped 15 s, 15-minute block at 10). Deltas: **PK becomes composite (email_norm, ip)** instead of unique-email (see Schema deltas), `login_attempts` written for unknown emails too (already handled — no `UsersId`). Added soft global per-email throttle: sum failures across IPs in the 15-minute window; ≥ 30 adds delay only, never a hard block. HTTP 423 is never returned — every failure mode is a uniform `401 invalid_credentials` + `Retry-After` when gated. Success resets the pair's row only.

**Rationale**: Revision 2 §§"login attempts and lockout" — per-(email,IP) keying means an attacker can never weaponize lockout against a victim from other addresses; uniform 401 removes the oracle that per-account hard blocks created.

## D9 — Fine-grained RBAC surface

**Decision**: authorization is permission-based only: `[AllowAnonymous]`/`[Authorize]`/`[HasPermission("…")]` everywhere; architecture tests forbid `IsInRole` / `[Authorize(Roles = …)]`. Effective permissions via existing `EffectivePermissions.Resolve`; 30 s absolute cache under `perms:{userId}` linked to a **global `CancellationChangeToken`** — any role/override mutation cancels the token, invalidating all users' entries at once. Privilege-escalation guard (FR-027): no admin can grant a permission string they do not themselves effectively hold. Student-scope roles may only hold `PermissionCatalog.StudentAllowed` (existing safety belt). Account elevation path (clarified Q1): self-registration → Student only; admins elevate existing users via admin endpoints.

**Rationale**: base design Alternatives #3 explicitly rejected role-based authorization; catalog already contains `session:multi`, `role:manage`, `user:manage` equivalents.

## D10 — Security events and audit redaction

**Decision**: new `security_events` store (event name, user subject, email, IP, timestamp, redacted details) with an isolated write path: `ISecurityAuditWriter` writes on its **own short-lived DbContext/connection** (`TransactionScopeOption.Suppress`-like isolation), so lockout/replay/OTP-failure/suspension/policy events survive any surrounding rollback. Redaction deny-list by column/field name: `password_hash`, `otp_hash`, `refresh_token_hash`, `previous_refresh_token_hash`, `token_hash`, `password`, `otp`, `refresh_token` — applied both in the EF `AuditSaveChangesInterceptor` and in event payloads. Retention: cleanup purges audit events older than 30 days (clarified Q3).

**Rationale**: FR-032 + §"Other flows" of Revision 2; base design's `ISecurityAuditWriter` rationale verbatim.

## D11 — Email delivery without an outbox

**Decision**: `IEmailQueue` = bounded `Channel<EmailMessage>` (capacity 1,000; full → drop + warning) consumed by a hosted `EmailSenderWorker` (10 s send timeout, 3 in-process retries at 1 s/5 s/15 s, drains on shutdown, logs redact recipient address, codes, and links). Crash may lose queued mail — accepted; users resend (`re-issue` path already implemented in `PendingRegistration.Reissue` with 60 s cooldown + 5/hour cap). Admin elevation runs queue an "account elevated" notice; already-registered emails queue an "already registered" notice instead of erroring.

**Rationale**: Revision 2 §7; PostgreSQL outbox rejected (Alternatives #5).

## D12 — Rate limiting placement

**Decision**: ASP.NET Core rate limiter registered with an **IP partition only** (20 req/min/IP across `/api/v1/auth/*`), the first app-level gate after forwarded-headers/log. Per-email limits deliberately live in handlers, because IP limiting runs before the body is read; per-email defenses are D8 (durable) plus FR-002's resend caps.

## D13 — Application-layer execution patterns

**Decision**: MediatR command/query separation exactly per the design's file list; each command validated by a FluentValidation validator registered through pipeline behaviors (password 8–128 NFC-normalized, email syntax, etc. — Domain `Email` value object remains the final authority). Result types reuse the existing `Result`/`SessionSummary` models. Transaction behavior: one DbContext transaction per command; exceptions to the rollback rule are explicit result objects (reuse detection D5, uniform-response handlers) per the design note "the reuse revoke must commit".

## D14 — Testing strategy

**Decision**: three suites, mirroring the design's verification plan and existing test layout:
- **Architecture tests** (new project): no `IsInRole`/`Authorize(Roles)`; every endpoint explicitly `[AllowAnonymous]`/`[Authorize]`/`[HasPermission]`; the session-middleware cache-hit branch never touches `AppDbContext`; layering rules (Domain references nothing).
- **Domain unit tests** (existing) extended: pepper-version handling, rehash flag, delayed/block gate math, grace-window rotation (platform time injected).
- **Integration tests** (new, Testcontainers PostgreSQL): SEC-01…SEC-18 verbatim from Revision 2; `WebApplicationFactory` with `BaseAddress = https://localhost` so `Secure` cookies round-trip; injected platform clock and email queue double for determinism.

## D15 — Forwarded headers / ingress trust (design open question — resolved in Revision 2)

**Decision**: `app.UseForwardedHeaders()` is the **first** middleware — before logging, rate limiting, auth. `ForwardedHeaders = XForwardedFor | XForwardedProto`, `ForwardLimit = 1`; loopback trusted by default, other proxy addresses listed in `Proxy:KnownProxies` config. nginx **overwrites** `X-Forwarded-For`/`X-Forwarded-Proto` (`proxy_set_header … $remote_addr`), Kestrel binds loopback only, HSTS at nginx. SEC-14 asserts spoofed forwarded headers from untrusted sources are ignored.

## D16 — Local development HTTPS

**Decision**: mkcert certificate for Kestrel HTTPS in dev (`dotnet dev-certs https --trust` is unreliable on Linux per Revision 2); optional local nginx with the same cert mirrors production. `WebApplicationFactory` clients use `https://localhost`. Secrets in dev come from user-secrets (`Auth:PasswordPepper:{v}`, `Auth:OtpHmacKey`, `Auth:JwtSigningKey`, `Site:Origin`); startup entropy checks gate them.

---

## Schema/code deltas (from existing `Auth_Initial` migration → new migration)

| # | Delta | Why |
|---|-------|-----|
| 1 | `login_attempts`: PK composite `(email_norm, ip)` + rename/adjust indexes (was unique on `email`); keep window/block indexes | D8 per-(email,IP) keying (Revision 2) |
| 2 | `users.password_hash` companion field `pepper_version` (smallint, derived from the PHC extension tag at read time — column for index-free audit and rehash predicate) | D1 versioning |
| 3 | New table `app_settings` (`key` text PK, `value` text, `updated_at`) seeded with `session.multi_max_concurrent=3` | D4 configurable cap |
| 4 | New table `security_events` (`id`, `event_name`, `user_id?`, `email?`, `ip?`, `details` JSONB redacted, `created_at`; index on `created_at` for purge) | D10 |
| 5 | `sessions.rotated_at` exists; no change. Suspension uses existing `users.status = 'suspended'` (the design's `is_suspended` maps onto the existing enum — no new column) | Existing model wins; design notation difference noted |
| 6 | New registrations/verifications record `IsStudentSession=true` and snapshot `EnforceSingleSession` (existing columns; no migration) | D4 |

## Unresolved items

None — all NEEDS CLARIFICATION candidates resolved as D1–D16 within the design's own decisions or documented accepted trade-offs (single generation replay detection, email loss on crash, single-instance cache semantics).
