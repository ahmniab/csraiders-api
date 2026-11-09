---
description: "Task list for Authentication & Session Management feature implementation"
---

# Tasks: Authentication & Session Management with Fine-Grained Access Control

**Input**: Design documents from `/specs/001-auth-session-management/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅ (decisions D1–D16), data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Included — the spec's Verification Plan explicitly mandates unit suites, integration SEC-01…SEC-18 (Testcontainers), and architecture tests.

**Organization**: Grouped by user story (US1…US8 from `specs/001-auth-session-management/spec.md`). Existing code base note: identity entities (`Session.cs`, `LoginAttempt.cs`, `PendingRegistration.cs`, `Role.cs`, `User.cs`), policies (`EffectivePermissions.cs`, `SessionPolicy.cs`), repository interfaces, `AuthRules.cs` constants, and the `Auth_Initial` migration already exist — tasks below never re-create them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1…US8)
- Exact file paths included in every description

## Path Conventions

Layered .NET solution at repo root: `src/CsRaiders.Api.{Domain,Application,Infrastructure,API}/`, tests in `tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Package wiring, config options, and empty test scaffolding — so user-story work starts on a compiling base.

- [X] T001 Add package references: `MediatR` + `MediatR.Contracts`, `FluentValidation.DependencyInjectionExtensions`, `Konscious.Security.Cryptography.Argon2` (version per research D1) to `src/CsRaiders.Api.Application/CsRaiders.Api.Application.csproj` and `src/CsRaiders.Api.Infrastructure/CsRaiders.Api.Infrastructure.csproj`; add `Serilog.AspNetCore` to `src/CsRaiders.Api.API/CsRaiders.Api.API.csproj`
- [X] T002 Add test packages: `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.Mvc.Testing`, `xunit.runner.visualstudio` to `tests/` project references; create `tests/CsRaiders.Api.IntegrationTests/CsRaiders.Api.IntegrationTests.csproj` (references API + Infrastructure) and `tests/CsRaiders.Api.ArchitectureTests/CsRaiders.Api.ArchitectureTests.csproj` (references all 4 src projects); add both to `CsRaiders.Api.sln`
- [X] T003 [P] Create options/config records `AuthOptions` (pepper versions `Auth:PasswordPepper:{n}`, `Auth:OtpHmacKey`, `Auth:JwtSigningKey`, cookie names, `Site:Origin`, `Proxy:KnownProxies`, `RateLimit:PerMinutePerIp=20`, `EmailQueue:Capacity=1000`, `Crypto:SemaphoreTimeoutSeconds=2`) in `src/CsRaiders.Api.API/Common/AuthOptions.cs` and bind them in `src/CsRaiders.Api.API/Program.cs`
- [X] T004 [P] Create `IPlatformClock` abstraction (`DateTimeOffset UtcNow`) in `src/CsRaiders.Api.Application/Common/Abstractions/IPlatformClock.cs` with `SystemPlatformClock` in `src/CsRaiders.Api.Infrastructure/Services/SystemPlatformClock.cs` (time injectable everywhere, research D14)

**Checkpoint**: solution builds, all 6 projects load.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Crypto primitives, schema delta, security eventing, email backbone, request pipeline, and caching. **MUST complete before any user story.**

**⚠️ CRITICAL**: No user story work until this phase is complete.

### Schema delta (research §Schema/code deltas)

- [X] T005 Create `SecurityEvent` entity (id, event_name, user_id?, email?, ip?, details JSONB redacted, created_at) in `src/CsRaiders.Api.Domain/Entities/SecurityEvent.cs` and `AppSetting` entity (key PK, value, updated_at) in `src/CsRaiders.Api.Domain/Entities/AppSetting.cs` per `specs/001-auth-session-management/data-model.md`
- [X] T006 Create EF configurations `SecurityEventConfiguration` + `AppSettingConfiguration` (created_at index for 30-day purge, citext email) in `src/CsRaiders.Api.Infrastructure/Persistence/Configurations/SecurityConfigurations.cs`; extend `src/CsRaiders.Api.Infrastructure/Persistence/AppDbContext.cs`
- [X] T007 Create migration `Auth_SecurityHardening`: composite PK `(email_norm, ip)` on `login_attempts` replacing unique email (research delta 1), add `pepper_version smallint NOT NULL DEFAULT 1` to `users` (delta 2), new `app_settings` table seeded `session.multi_max_concurrent=3` (delta 3), new `security_events` table (delta 4) — run `dotnet ef migrations add Auth_SecurityHardening` in `src/CsRaiders.Api.Infrastructure` and verify against contracts in `specs/001-auth-session-management/data-model.md`
- [X] T008 Add `ISecurityEventRepository` + `IAppSettingRepository` in `src/CsRaiders.Api.Application/Common/Interfaces/Repositories/` and implementations in `src/CsRaiders.Api.Infrastructure/Persistence/Repositories/` (per method contract: recorded failures, row sum; get/set settings)

### Crypto primitives (research D1, D2)

- [X] T009 Create interfaces `IPasswordHasher`, `IOtpGenerator`, `ITokenService`, `ISecurityAuditWriter`, `IEmailQueue` in `src/CsRaiders.Api.Application/Common/Interfaces/Security/` — signatures per plan.md structure and data-model contract (hash: NFC-normalize → HMAC pepper → Argon2id m=19456,t=2,p=1 → PHC `$argon2id$v=19…$p=<ver>`; verify returns `IsValid, NeedsRehash`)
- [X] T010 Implement `Argon2idPasswordHasher` in `src/CsRaiders.Api.Infrastructure/Security/Argon2idPasswordHasher.cs` per D1: bounded `SemaphoreSlim` (core count), 2 s wait timeout → returns `HashOutcome.Unavailable` (maps to 503), dummy-hash verification path with identical cost, pepper-version handling parsing/writing `p=<n>` PHC extension; `PasswordHashGuard` extension consistent
- [X] T011 [P] Implement `HmacOtpGenerator` in `src/CsRaiders.Api.Infrastructure/Security/HmacOtpGenerator.cs` per D1/FR-002: `RandomNumberGenerator.GetInt32(0, 1_000_000)` 6-digit string; `HMACSHA256(Auth:OtpHmacKey, email:otp)` compared via `CryptographicOperations.FixedTimeEquals`
- [X] T012 Implement `TokenService` in `src/CsRaiders.Api.Infrastructure/Security/TokenService.cs` per D2: 15-min HS256 JWT pinned (`sub`, `sid`, `iat`, `exp` only — no role/permission claims), 32-byte Base64Url refresh token + SHA-256 hash helper; registration in `src/CsRaiders.Api.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`

### Security audit + email backbone (research D10, D11, FR-032, FR-035)

- [X] T013 Implement `SecurityAuditWriter` in `src/CsRaiders.Api.Infrastructure/Services/SecurityAuditWriter.cs` per D10: isolated short-lived `AppDbContext` scope (survives surrounding rollback), fields serialized with deny-list redaction (`password_hash`, `otp_hash`, `refresh_token_hash`, `previous_refresh_token_hash`, `token_hash`, `password`, `otp`, `refresh_token`)
- [X] T014 [P] Create `EmailMessage` record in `src/CsRaiders.Api.Domain/Entities/EmailMessage.cs` (purpose enum: registration_otp, password_reset, account_notice) and bounded `ChannelEmailQueue` in `src/CsRaiders.Api.Infrastructure/Services/ChannelEmailQueue.cs` (capacity 1000, full → drop + log warning)
- [X] T015 Create `EmailSenderWorker` hosted service in `src/CsRaiders.Api.Infrastructure/Services/EmailSenderWorker.cs` per D11: 10 s send timeout, 3 attempts (1 s/5 s/15 s), drains channel on shutdown, logs redact recipient/OTP/link; pluggable `IEmailSender` interface + dev console sender stub in same folder
- [X] T016 Modify `src/CsRaiders.Api.Infrastructure/Interceptors/AuditSaveChangesInterceptor.cs` to enforce the column-name deny-list redaction on all audited JSON payloads (if file exists only as plan stub, create it) — deny-list from `contracts/errors-and-cookies.md` §3 notes

### Request pipeline + auth plumbing (research D2, D3, D12, D15; FR-015, FR-016, FR-021, FR-030)

- [X] T017 Wire `Program.cs` ordering in `src/CsRaiders.Api.API/Program.cs` per D15/D12: `UseForwardedHeaders` first (XForwardedFor|XForwardedProto, ForwardLimit 1, `Proxy:KnownProxies`) → Serilog request logging with redaction → `AddRateLimiter` IP partition (20 req/min/IP on `/api/v1/auth/*`) → `AddJwtBearer` (HS256 allow-list, `ClockSkew=30s`, issuer/audience) with `sid` claim mapping → session middleware → authorization. Include startup entropy checks per research D16 (refuse to start on weak keys/missing Site:Origin)
- [X] T018 Implement `SessionValidationMiddleware` in `src/CsRaiders.Api.API/Middleware/SessionValidationMiddleware.cs` per D3/FR-015/FR-021: cache key `session:status:{sid}` with **absolute 5 s TTL** (not sliding), cached value carries `expires_at`/`idle_expires_at` re-checked every hit, miss → indexed lookup by injected repository interface (not `AppDbContext` on hot path), revoked/expired → evict, clear cookies, `401 session_superseded`; accept injected `ISessionCacheHook` eviction callback (created in T020)
- [X] T019 [P] Create `ISessionStatusReader` application interface in `src/CsRaiders.Api.Application/Common/Interfaces/Security/ISessionStatusReader.cs` with `SessionRepository` read implementation (`SessionRepository` exists — add status-read method) so the middleware's DB fallback uses application interfaces, per architecture-test rule
- [X] T020 [P] Create `ISessionCacheHook` (evict `session:status:{sid}` post-commit) + `MemorySessionCacheHook` in `src/CsRaiders.Api.Infrastructure/Services/SessionCacheHook.cs` and register in DI — the single eviction point every later revoke path calls (`contracts/errors-and-cookies.md` §1)

### MediatR scaffold

- [X] T021 Enable `AddMediatR` + FluentValidation pipeline behavior in `src/CsRaiders.Api.Application/DependencyInjection/ApplicationServiceExtensions.cs` (assembly scan), with a transaction behavior (DbContext transaction per command; commands that must "commit even on failure result" — e.g. refresh reuse — return result objects instead of throwing, research D13/D5) and a validation behavior producing RFC 7807 `validation_failed` responses

**Checkpoint**: `dotnet build` clean; pipeline orders compile; no story endpoints yet; `dotnet test tests/CsRaiders.Api.Domain.UnitTests` still green.

---

## Phase 3: US1 — Student Account Registration & Email Verification (P1)

**Story goal**: A prospective student registers, receives an OTP by email, verifies it, becomes a Student account.
**Independent test** (from spec US1): register → resend → verify flows, uniform responses, purge on success.

**Fr-optimal order**: validators → handlers → endpoints → tests.

- [X] T022 [US1] Create `RegisterCommand`+`Validator`+`Handler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/Register/{RegisterCommand.cs,RegisterCommandValidator.cs,RegisterCommandHandler.cs}`) per FR-001/FR-003/FR-005/FR-026-clarifications: password 8–128 NFC; branch on existing user (queue "already registered" notice) vs pending (never overwrite stored password hash — call `PendingRegistration.Reissue`, cooldown 60 s + 5/h caps) vs new (`PendingRegistration.Start`); always return generic `202` result; register rates validated by `AuthRules` (`specs/001-auth-session-management/data-model.md`)
- [X] T023 [US1] Create `VerifyOtpCommand`+`Handler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/VerifyOtp/…`) per FR-002/FR-026 and `PendingRegistration.Verify` contract: fixed-time compare of candidate hash, attempts_left decrement persisted even on rejection (SEC-03), on match create `User.RegisterVerified` with Student role preset + purge row in the same transaction, emit `security_events` on failure
- [X] T024 [US1] Create `ResendOtpCommand`+`Handler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/ResendOtp/…`) per FR-004: fresh `otp_hash` replaces old one, `attempts_left` reset to 5, expiry re-extended 10 min, uniform 202 for unknown/pending/registered emails, cooldown + hourly caps enforced via `PendingRegistration.Reissue`
- [X] T025 [US1] Write unit tests for handlers' branch matrix in `tests/CsRaiders.Api.Application.UnitTests/Auth/RegisterResendVerifyTests.cs` (create the folder): known/unknown/pending parity, cap exhaustion, cooldown, purge-on-verify, no-overwrite hash equality (SEC-15/16 domain logic)
- [X] T026 [US1] Implement AuthController registration phase in `src/CsRaiders.Api.API/Controllers/AuthController.cs`: `POST /api/v1/auth/register`, `/auth/verify-otp`, `/auth/resend-otp` per `specs/001-auth-session-management/contracts/openapi.yaml` (verify uses CSV `^[0-9]{6}$` pattern; 202 responses per `AuthAcceptedResponse`; 422 validation; 503 capacity)
- [X] T027 [US1] Add integration tests `tests/CsRaiders.Api.IntegrationTests/RegistrationEndpointTests.cs` using Testcontainers + a capturing fake `IEmailSender`: SEC-02 (identical response+timing envelope for unknown email), SEC-03 (5 fails then correct code → 400 `invalid_or_expired_otp`, attempts persisted), SEC-15/16 (replaces code, old dead, hash untouched), 202-uniformity assertions
- [X] T028 [US1] Architecture-test additions counting only US1 surface in `tests/CsRaiders.Api.ArchitectureTests/AuthEndpointAuthorizationTests.cs` (file created here; more rules added later): new endpoints have explicit `[AllowAnonymous]`/`[Authorize]`/`[HasPermission]`

**Checkpoint**: US1 end-to-end demoable via quickstart §3 rows 1–2.

---

## Phase 4: US2 — Login with Credential Verification & Durable Brute-Force Defense (P1)

**Story goal**: Correct credentials → tokens; wrong credentials → gate-first delays/blocks that survive restarts; uniform anti-enumeration.
**Independent test** (spec US2): gate math, 10-fail block, cross-IP unlock, suspended=uniform 401.

- [ ] T029 [US2] Implement `LoginAttemptTracker` in `src/CsRaiders.Api.Infrastructure/Services/LoginAttemptTracker.cs` wiring `LoginAttempt.RegisterFailure/Evaluate/Reset` through `login_attempts` per D8: insert-or-gate rows keyed `(email_norm, ip)`, unknown emails recorded too, per-email soft cap (sum ≥30 failures in 15-min window across IPs → delay only), success-reset semantics
- [ ] T030 [US2] Create `LoginCommand`+`Validator`+`Handler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/Login/…`) per FR-006..FR-014: gate-first (D8 — password work only when open), unknown-email dummy Argon2id verification (D1), suspended `UserStatus` never distinguished (FR-011 `401 invalid_credentials`), per-user advisory lock `pg_advisory_xact_lock(hashtextextended(user_id, 0))` before supersede/insert, session opened via `Session.Open` with `EnforceSingleSession` snapshot and `DeviceFp`, password rehash on `NeedsRehash` (FR-006 transparent upgrade), `security_events` for lockouts
- [ ] T031 [US2] Implement multi-session predicate + cap lookup `IAppSettingsProvider.GetMultiSessionCap()` wrapper over `app_settings` in `src/CsRaiders.Api.Infrastructure/Services/AppSettingsProvider.cs` (default `AuthRules.DefaultMultiSessionCap`, short read cache + eviction on admin write — later story uses the setter)
- [ ] T032 [US2] Extend `src/CsRaiders.Api.API/Controllers/AuthController.cs`: `POST /api/v1/auth/login` per `contracts/openapi.yaml` — issues access token body + `Set-Cookie __Host-csraiders_rt; Secure; HttpOnly; SameSite=Strict; Path=/` (`ISessionCookieWriter` abstraction in `src/CsRaiders.Api.Application/Common/Abstractions/ISessionCookieWriter.cs`, impl in `src/CsRaiders.Api.API/Services/SessionCookieWriter.cs`) + `csraiders_dfp` issuance (D6), uniform `401 invalid_credentials` + `Retry-After` for gated attempts, 429 for limiter, 503 for crypto capacity
- [ ] T033 [US2] Integration tests `tests/CsRaiders.Api.IntegrationTests/LoginEndpointTests.cs`: SEC-04 (10 failures bind (email,IP); block via uniform 401 + Retry-After; restart host = WebApplicationFactory reset with same DB → block persists; different IP succeeds), unknown/suspended/wrong-password parity, tokens claim-set check (`sub`,`sid` only), rehash-on-old-params unit anchor
- [ ] T034 [US2] Integration tests `tests/CsRaiders.Api.IntegrationTests/LoginConcurrencyTests.cs`: SEC-05 (10 `Task.WhenAll` logins → exactly 1 active session row, no leak of 23505), soft per-email cap pacing test for distributed-attack path (FR-009 delay-only)

**Checkpoint**: US2 demoable; spec SC-002 latency envelope validated in the covering tests.

---

## Phase 5: US3 — Single Active Session per Student (P1)

**Story goal**: Supersession always immediate and race-free; capped multi-session for `session:multi` holders.
**Independent test** (spec US3): supersede within ≤5 s; 10-way race → 1 survivor; cap rotation `cap_exceeded`.

- [ ] T035 [US3] Complete supersede/cap path inside `LoginCommandHandler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/Login/LoginCommandHandler.cs`) using `SessionPolicy.Decide` (exists): `RevokeAll` when single-session (revoked rows get `revoke_reason=superseded`, evict their cache entries via T020 after commit), `RevokeOldest` overflow arithmetic for `session:multi` holders (`cap_exceeded`), one retry on `23505` for the partial-unique backstop `uq_sessions_one_active` (research D4)
- [ ] T036 [US3] Integration tests `tests/CsRaiders.Api.IntegrationTests/SessionSupersessionTests.cs`: SEC-06 (4 sequential logins as multi-holder via `user_permissions` grant of `session:multi` → oldest revoked `cap_exceeded` and exactly N−1 active after), SEC-10-style cache-respect check (device request each second while other device logs in → next request 401 `session_superseded` — proves eviction, not just TTL), 5-second worst-case bound assertion (timer-based)
- [ ] T037 [US3] Add session-cap integration guard test in `tests/CsRaiders.Api.IntegrationTests/SessionSupersessionTests.cs` for partial-unique race (concurrent logins reaching the index under advisory-lock retry loop) — zero unhandled `23505` escapes

**Checkpoint**: spec SC-003/SC-004 measurable nowhere else covering supersession; device B wins, device A dies ≤5 s.

---

## Phase 6: US4 — Refresh Token Rotation & Theft Detection (P1)

**Story goal**: Rotation every use, benign retry within grace, replay-after-grace kills the session irreversibly; cookie-acting endpoints CSRF-gated.
**Independent test** (spec US4): rotation, ≤15 s grace retry passes, post-grace replay → `session_compromised` survives rollback attempts.

- [ ] T038 [US4] Create `RefreshTokenCommand`+`Handler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/Refresh/…`) per D5/FR-018/FR-019 and existing `Session.TryRotate`: `SELECT … FOR UPDATE` (SessionRepository retry contract) matching current or previous hash; previous-hash match with `RotatedAt ≤ now−15 s` → benign rotate-again, else revoke (`reuse_detected`) + **commit regardless** by returning `RefreshOutcome` result object (never throws, per research D5), evict cache post-commit + `security_events` write; returns new pair or error code map per contracts (`invalid_grant`, `session_superseded`, `session_compromised`)
- [ ] T039 [US4] Implement `ISessionCookieWriter` full usage: clear-refresh Cookie emission on all failure branches + rotate Set-Cookie in `src/CsRaiders.Api.API/Services/SessionCookieWriter.cs` per `contracts/errors-and-cookies.md` §1 (`__Host-` prefix rules honored)
- [ ] T040 [US4] Create `OriginAndHeaderValidationFilter` in `src/CsRaiders.Api.API/Filters/OriginAndHeaderValidationFilter.cs` per D7/FR-031: applies only to `/auth/refresh` & `/auth/logout`; requires Origin=Site:Origin (Referer fallback, both missing → 403), `X-Requested-With: csraiders`, `Content-Type: application/json`
- [ ] T041 [US4] Extend `src/CsRaiders.Api.API/Controllers/AuthController.cs`: `POST /api/v1/auth/refresh` wired with T040 filter + dfp device verification (missing/changed `csraiders_dfp` → `401 session_superseded` + clears), per `contracts/openapi.yaml` and `errors-and-cookies.md` §2
- [ ] T042 [US4] Add JWT hardening test file `tests/CsRaiders.Api.IntegrationTests/JwtSecurityTests.cs`: SEC-13 (expired token, wrong audience, `alg: none`, wrong-key rejected; HS256 allow-list respected)
- [ ] T043 [US4] Integration tests `tests/CsRaiders.Api.IntegrationTests/RefreshRotationTests.cs`: SEC-08 (post-grace replay revokes session, verification persists after simulated rollback attempt in wrapper test), SEC-11 (≤15 s retry rotates), SEC-12 (missing/foreign Origin, missing custom header → 403; cookie attribute assertions Secure/HttpOnly/SameSite=Strict via response parsing), device-mismatch path (D6)

**Checkpoint**: US4 demoable via quickstart §3 row 5; token-theft drill mirrors spec SC-005 exactly.

---

## Phase 7: US5 — Self-Service Session Visibility & Termination (P2)

**Story goal**: Users list their sessions and revoke one or all other sessions.
**Independent test** (spec US5): list shows only own active sessions; revoked device dies immediately.

- [ ] T044 [US5] Create `AuthenticatedOnly` policy in `src/CsRaiders.Api.API/Authorization/AuthPolicies.cs` (sid-claim principal) + `GetSessionSummariesQuery`+`Handler` (`src/CsRaiders.Api.Application/Features/Sessions/Queries/GetActiveSessionsQuery.cs`) mapping `SessionSummary` (device label, ip, user agent, created/last-seen — never tokens/hashes)
- [ ] T045 [US5] Create `RevokeSessionCommand` + `RevokeAllOtherSessionsCommand`+`Handlers` (`src/CsRaiders.Api.Application/Features/Sessions/Commands/RevokeSessionCommands.cs`) per FR-022/US5: revoke specific one the caller owns (`SessionRepository` active-row check) or all-but-current, reason `logout` for self-requested, evict via T020 post-commit, `security_events` recorded
- [ ] T046 [US5] Implement `POST /api/v1/auth/logout` in `src/CsRaiders.Api.API/Controllers/AuthController.cs`: session revoked (`logout`), cache evicted, both cookies cleared, Origin+header validation applied (reuse `OriginAndHeaderValidationFilter` from T040), per `contracts/openapi.yaml`
- [ ] T047 [US5] Create `MeSessionsController` in `src/CsRaiders.Api.API/Controllers/MeSessionsController.cs` per `contracts/openapi.yaml`: `GET /api/v1/me/sessions`, `DELETE /api/v1/me/sessions/{sessionId}`, `POST /api/v1/me/sessions/revoke-others` — all with `[Authorize(Policy=AuthenticatedOnly)]`
- [ ] T048 [US5] Integration tests `tests/CsRaiders.Api.IntegrationTests/SessionManagementEndpointTests.cs`: list hides other users' sessions; revoke specific → supp. device's next request `401 session_superseded`; revoke-others keeps current; logout clears cookie + revokes (SEC-04'refresh-style' cookie-clear assertions from `errors-and-cookies.md`)

**Checkpoint**: quickstart §3 row 10 demoable.

---

## Phase 8: US6 — Password Change & Self-Service Reset (P2)

**Story goal**: Change password (current required, other sessions revoked); forget/reset cycle uniform + all-sessions revoked.
**Independent test** (spec US6): change-password flow; forget→reset→single-use-invalidation chain.

- [ ] T049 [US6] Create `ForgotPasswordCommand`+`Handler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/ForgotPassword/…`) per FR-023: uniform 202/IP-limited, 30-min single-use SHA-256 token via `PasswordResetToken`, supersedes prior unused tokens, queues email (password_reset purpose), `PendingRegistration`-cap parity for hourly abuse
- [ ] T050 [US6] Create `ResetPasswordCommand`+`Handler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/ForgotPassword/…` per plan): constant-time token compare, single-use consume, `User.ChangePassword` + Argon2id upgrade, revoke **all** sessions (`password_reset`) + evictions, success resets `login_attempts` for the user's email rows (existing account unlock rule D8), `security_events` for issuance and completion
- [ ] T051 [US6] Create `ChangePasswordCommand`+`Handler` (`src/CsRaiders.Api.Application/Features/Auth/Commands/ChangePassword/…`) per FR-024: current password verified first (gate not required), other sessions revoked `password_changed` (current survives), evictions + events
- [ ] T052 [US6] Extend `src/CsRaiders.Api.API/Controllers/AuthController.cs`: `/auth/forgot-password`, `/auth/reset-password`, `/auth/change-password` per `contracts/openapi.yaml` (change-password requires `[Authorize]`; reset-password anonymous)
- [ ] T053 [US6] Integration tests `tests/CsRaiders.Api.IntegrationTests/PasswordFlowTests.cs`: uniform 202 unknown/known email and identical timing; new request invalidates prior token (FR-023); reuse returns `400 invalid_or_expired_...` stable code; reset revokes all sessions incl. current device; change-password keeps current alive only; `login_attempts` reset on reset success

**Checkpoint**: quickstart §3 row 8 demoable.

---

## Phase 9: US7 — Fine-Grained Permissions with Live Policy Reconciliation (P2)

**Story goal**: `[HasPermission]` everywhere, deny-wins, 30 s global invalidation token, admin surface with escalation guards, reconciliation on `session:multi` loss.
**Independent test** (spec US7): permission flip visible ≤30 s without re-login; downgrade reconciles sessions; escalation guard blocks.

- [ ] T054 [US7] Create `[HasPermissionAttribute]` + `PermissionAuthorizationHandler` + `PermissionRequirement` in `src/CsRaiders.Api.API/Authorization/{HasPermissionAttribute.cs,PermissionAuthorizationHandler.cs}` per D9/FR-025: reads `perms:{userId}`, 30 s absolute TTL linked to a **global `CancellationChangeToken`** source exposed by `IRbacInvalidationToken` (`src/CsRaiders.Api.Application/Common/Interfaces/Security/IRbacInvalidationToken.cs`) — any mutation cancels it (evicts all users at once per FR-026/FR-027-adjacent needs), miss → `EffectivePermissions.Resolve` over roles+overrides
- [ ] T055 [US7] Create `UpdateRolePermissionsCommand` + `SetUserPermissionOverrideCommand` + `ElevateUserRolesCommand`+`Handlers` (`src/CsRaiders.Api.Application/Features/Admin/…` per plan structure): role permission replacement with `RoleScope.Student` → `PermissionCatalog.StudentAllowed` safety belt; user override set-clear; role-membership replacement for elevation (clarified Q1 — self-registration stays Student-only, FR-037); **escalation guard** (FR-027): grantor must effectively hold every granted string, checked post-DENY; cancel IRbacInvalidationToken + reconcile on `session:multi` change; `security_events` per change
- [ ] T056 [US7] Implement `SessionReconciliationService` in `src/CsRaiders.Api.Infrastructure/Services/SessionReconciliationService.cs` per D4/FR-028: one transaction under the user's advisory lock — revoke older actives `policy_reduced`, then set newest `enforce_single_session=true` (order preserves `uq_sessions_one_active`), evict affected cache entries after commit
- [ ] T057 [US7] Create `AdminRolesController` + `AdminUsersController` in `src/CsRaiders.Api.API/Controllers/…` per `contracts/openapi.yaml`: `[HasPermission("role:manage")]` / `[HasPermission("user:manage")]` on put/post endpoints (admin-users roles updates use `user:manage`), 403 (missing permission or escalation guard), 404 unknown role/user
- [ ] T058 [US7] Add Application unit tests `tests/CsRaiders.Api.Application.UnitTests/Admin/RbacGuardTests.cs`: escalation guard matrix (grant unheld fails, grant held passes; DENY never permits), student-scope belt, IRbacInvalidationToken cancel invocation contract
- [ ] T059 [US7] Integration tests `tests/CsRaiders.Api.IntegrationTests/RbacReconciliationTests.cs`: SEC-18 (role change reflects for holders ≤1 request later — token cancel not TTL-dependent), SEC-07 (session:multi loss → 2× `policy_reduced` + survivor single-session while 3 sessions active), guard 403s on admin endpoints, admin writes of `app_settings` evict the cap provider cache from T031

**Checkpoint**: quickstart §3 rows 4 + 7 + `accounts.elevation` path demoable.

---

## Phase 10: US8 — Administrative Suspension & Data Hygiene (P3)

**Story goal**: Suspend/reactivate with instant lockout; scheduled cleanup purges exactly FR-034's set (incl. 30-day audit retention, clarified Q3).
**Independent test** (spec US8): suspend kills sessions, blocks login uniformly; cleanup job removes only aged artifacts.

- [ ] T060 [US8] Create `SuspendUserCommand` + `ReactivateUserCommand`+`Handlers` (`src/CsRaiders.Api.Application/Features/Admin/Users/…`): `User.Suspend/Reactivate`, revoke **all** sessions `account_disabled` + evictions, guard against deleting suspended state conflict per `User.cs` rules, `security_events` (`account_suspended`/`account_reactivated`); endpoint `POST /api/v1/admin/users/{userId}/suspend` (+reactivate sibling) under `[HasPermission("user:manage")]` in `src/CsRaiders.Api.API/Controllers/AdminUsersController.cs`
- [ ] T061 [US8] Create `AuthCleanupWorker` hosted service in `src/CsRaiders.Api.Infrastructure/Services/AuthCleanupWorker.cs` per FR-034: hourly pass deleting sessions revoked/expired >30 d, pending registrations expired, reset tokens used/expired >7 d, `login_attempts` inactive >24 h, `security_events` >30 d — batched, cancellable, logged counts (no-op on overlap guard)
- [ ] T062 [US8] Integration tests `tests/CsRaiders.Api.IntegrationTests/SuspensionCleanupTests.cs`: SEC-17 (suspension revokes sessions instantly; suspended login = exactly `invalid_credentials` 401), cleanup unit-anchored assertions (aged removed, current untouched; audit 30-day retention honored)

**Checkpoint**: quickstart §3 row 9 demoable.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Architecture conformance, observability set (FR-038), load sanity, docs.

- [ ] T063 Add architecture test rules to `tests/CsRaiders.Api.ArchitectureTests/`: (a) zero `IsInRole` / `[Authorize(Roles …)]` across solution; (b) every mapped endpoint explicitly `[AllowAnonymous]`/`[Authorize]`/`[HasPermission]`; (c) middleware hot-path (CacheHit branch) never injects `AppDbContext` (research D14); (d) Domain project references nothing but BCL
- [ ] T064 [P] Add redaction sweep integration test `tests/CsRaiders.Api.IntegrationTests/SecretRedactionTests.cs`: capture structured logs + `security_events.details` payloads across register/fail-login/refresh/lockout/reset flows asserting absence of deny-list fields from `contracts/errors-and-cookies.md` §5 (SC-008)
- [ ] T065 [P] Add observability counters (FR-038 / contracts §5) in `src/CsRaiders.Api.Infrastructure/Services/AuthMetricsRecorder.cs` + wire emits at: lockouts, blocks, OTP failures/sends, token reuse, session revocations by reason, queue drops/retries, cache hit/miss (session + permission caches); assertion test in `tests/CsRaiders.Api.IntegrationTests/ObservabilityTests.cs` counters present after scenarios
- [ ] T066 [P] Cache-load sanity harness `tests/CsRaiders.Api.IntegrationTests/CacheThroughputTests.cs`: sustained mixed authenticated traffic (in-proc load) showing ≥95% cache-hit ratio with 5 s absolute TTL preserved and revocation ≤5 s under way (SC-007/SC-003) — no external Redis required (research D3)
- [ ] T067 [P] Document ops: update `README.md` (or create `docs/auth-operations.md`) — startup gates, config keys, cookie contract pointer, cleanup/gate retention table, single-instance assumption + scaling note (research D3/D11/D16)
- [ ] T068 Run full quickstart gate `specs/001-auth-session-management/quickstart.md` §3 scenario table end-to-end and record SEC-01…SEC-18 pass results in that file's §4 checklist

---

## Dependencies

- **Phase 1 → Phase 2 → all user stories** (strict; foundational blocks everything)
- **US1** (T022–T028) → independent; needs T009–T015, T021, endpoints infra
- **US2** (T029–T034): needs T010, T012, T017–T020, T021 → then US3, US4, US6 depend on login-created sessions
- **US3** (T035–T037): builds directly on US2 output; do second
- **US4** (T038–T043): needs US2 session issuance + T018/T020; can start in parallel with US3 after US2
- **US5** (T044–T048): needs US2 + US4 (cookie machinery); after US4 is natural
- **US6** (T049–T053): needs US1 identity + US2 revocation helpers; after US3 recommended (session revoke reuse)
- **US7** (T054–T059): needs US2 (login decisions) + T020; SEC-07 lives here — start after US3 so reconciliation composes with tested supersession
- **US8** (T060–T062): needs US7 admin controllers pattern; after US7
- **Polish** (T063–T068): last; T068 depends on all tests passing

### Suggested story completion order

`US1 → US2 → US3 → US4 → US5 → US6 → US7 → US8 → Polish`

### Parallel execution examples

- After Phase 2 checkpoint: US1 and (nothing else — US2 gates remaining stories). Inside US1, T025 ∥ T026 (tests vs endpoint) after T022–T024.
- After US2: run **US3 (T035–T037)** and **US4 (T038–T043)** in parallel — different files (Login handler completion vs Refresh + filter + cookie writer).
- After US3+US4: **US5 (T044–T048)** ∥ **US6 (T049–T053)** — disjoint controllers/commands; then **US7 (T054–T059)**, then **US8 (T060–T062)**.
- Inside US7: T058 ∥ T059 only after T054–T057.
- Polish: T063–T067 mutually parallel once all stories are green; T068 strictly last.
- Within Phase 2: T009–T012 mutually largely parallel (new files); T013 ∥ T014∥T015; T017–T020 serial after configs; T021 last of pairing with T008/repository completion.

## Implementation strategy

1. **MVP first** = Phase 1 + Phase 2 + **US1 + US2 + US3 + US4** (the four P1 stories): registration → login → single-session → refresh is the smallest coherent authentication loop; demoable after each story via the relevant quickstart row.
2. **Incremental delivery**: each phase ends green (`dotnet build` + covering tests); no placeholder controllers ever committed under `[Authorize]`-less ambiguity (architecture tests enforce).
3. **Existing-code leverage**: never duplicate policy/entity logic already in Domain; new application/infrastructure code composes it (`SessionPolicy.Decide`, `Session.TryRotate`, `PendingRegistration.Verify/Reissue`, `LoginAttempt.*`, `EffectivePermissions.Resolve`).
4. **Security invariants to hold at every commit**: no secrets in logs/audit; no 423; no roles-in-JWT; uniform anti-enumeration responses; revocation ≤5 s worst case; the reuse-detection result-object rule (revocation must survive transaction behavior).
