# Implementation Plan: Authentication & Session Management with Fine-Grained Access Control

**Branch**: `001-auth-session-management` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-auth-session-management/spec.md`

**Design documents (authoritative)**:
- `docs/designs/authentication_and_session_management.md` (base, 2026-11-07)
- `docs/designs/CS Raiders — Authentication & Session Management (Revision 2).md` (supersedes base)

## Summary

Implement the authentication subsystem for the CS Raiders ASP.NET Core API: OTP-verified registration with timing-uniform anti-enumeration, durable per-(email, IP) brute-force defense, single-session-by-default account concurrency with `session:multi` capping, 15-minute access tokens + 30-day rotating refresh tokens with grace-windowed replay detection, a request-hot-path session validation cache (≤5s absolute TTL with explicit eviction), permission-based RBAC with per-user deny-wins overrides, session ownership APIs (list/revoke), password change/reset, account suspension, a started email delivery queue + cleanup workers, and audited security events with strict secret redaction.

The approach builds on the already-migrated identity schema (`users`, `roles`, `user_roles`, `user_permissions`, `sessions`, `login_attempts`, `pending_registrations`, `password_reset_tokens`), the domain policies (`EffectivePermissions.Resolve`, `SessionPolicy.Decide`, `Session.TryRotate`, `LoginAttempt.RegisterFailure`, `PendingRegistration.Verify`) and repository interfaces already present, adding the application command/query layer, the infrastructure security services, the API middleware/authorization surface, and a small schema delta.

## Technical Context

**Language/Version**: C# / .NET 10.0 (`global.json` pins SDK 10.0.113, rollForward latestMinor); ASP.NET Core 10 hosting model

**Primary Dependencies**:
- EF Core 10.0.14 + Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 + EFCore.NamingConventions 10.0.1 (existing)
- Konscious.Security.Cryptography.Argon2id (design-specified password hashing primitive)
- MediatR + FluentValidation (to wire into the existing `AddApplicationServices` placeholder)
- `Microsoft.AspNetCore.RateLimiter` (built into ASP.NET Core; no extra package on .NET 10)
- `System.Threading.Channels` (in-process email queue; BCL)
- xUnit + Testcontainers.PostgreSql (integration tests per design's verification plan)

**Storage**: PostgreSQL 16 — sole authoritative store (no Redis, no outbox, no broker for Milestone 0); `citext` extension already enabled

**Testing**: xUnit solution tests — Domain unit tests (exists: `tests/CsRaiders.Api.Domain.UnitTests`), new Architecture Tests, and Application/Integration Testcontainers suites (`SEC-01` … `SEC-18` from the design)

**Target Platform**: Single Linux host; one nginx reverse proxy (TLS termination, `X-Forwarded-For` overwrite, loopback/private-network binding) → Kestrel; SPA served at `/`, API under `/api` on one origin (no CORS)

**Project Type**: Layered N-tier solution (existing): `Domain` ← `Application` ← `Infrastructure` ← `API`

**Performance Goals**: Cache-hit session validation < 1 ms; P99 < 20 ms core endpoints; login responses < 1 s at P95 under normal load; ≥ 95% reduction in per-request session reads vs. no-cache baseline at 500–1,500 RPS

**Constraints**: Single-instance deployment (in-process caches + email queue assume one process); revocation effective immediately in the common case, ≤ 5 s worst case; zero secrets in logs/audit; password hashing bounded by CPU-core semaphore; uniform anti-enumeration responses (`401` for all login failures — no 423); refresh rotation grace window 15 s; only one retired refresh hash per session

**Scale/Scope**: Educational platform users ≈ thousands of students; ~26 new/modified production files per the design's implementation list across 4 layers; auth surface = 9 public endpoints + 2 self-service endpoints + 4 admin endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Status**: `.specify/memory/constitution.md` contains only the unratified Speckit scaffold (placeholder `[PROJECT_NAME]`, `[PRINCIPLE_*]` entries; no project-specific principles have been ratified). No enforceable gates exist, so **no violations can be assessed** — this is recorded rather than treated as a pass.

- Project principles found: none (constitution is the empty template)
- Assumed working constraints (= the practice the design docs themselves mandate, applied as pseudo-gates):
  1. **Test-first for security flows** — verification plan SEC-01…SEC-18 must be executable from `quickstart.md` — PASS (post-Phase 1: yes, see `quickstart.md`)
  2. **Layering** — Domain has no external deps; Application talks only to interfaces; Infrastructure + API depend inward — PASS (post-Phase 1: architecture test design in `research.md` D14 enforces it)
  3. **No secrets in artifacts** — plan/contracts must not embed real pepper/HMAC/signing keys; config keys referenced by name only — PASS

Post-design re-check: still PASS; no complexity-tracking violations required.

## Project Structure

### Documentation (this feature)

```text
specs/001-auth-session-management/
├── plan.md              # This file
├── research.md          # Phase 0 output — decisions D1–D16
├── data-model.md        # Phase 1 output — entities, deltas, state machines
├── quickstart.md        # Phase 1 output — end-to-end validation guide
├── contracts/           # Phase 1 output
│   ├── openapi.yaml     # Auth/session/admin HTTP surface
│   └── errors-and-cookies.md  # Error taxonomy, cookie/header contract
└── spec.md, checklists/, ...
```

### Source Code (repository root)

```text
src/
├── CsRaiders.Api.Domain/                  # exists — pure models, no framework refs
│   ├── Constants/   AuthRules.cs, PermissionCatalog.cs, RolePresets.cs
│   ├── Entities/    User, Role, UserRole, UserPermission, Session,
│   │                LoginAttempt, PendingRegistration, PasswordResetToken
│   │                + NEW: SecurityEvent, AppSetting (deltas)
│   ├── Enums/       AuthEnums.cs
│   ├── Policies/    EffectivePermissions.cs, SessionPolicy.cs
│   └── ValueObjects/Email.cs, PasswordHashGuard.cs
├── CsRaiders.Api.Application/             # interfaces + command/query layer (new content)
│   ├── Common/Interfaces/Security/  IPasswordHasher, IOtpGenerator, ITokenService,
│   │                                ISecurityAuditWriter, IEmailQueue, ILoginAttemptTracker,
│   │                                ISessionCacheHook, ISystemClock (NEW)
│   ├── Common/Interfaces/Repositories/           # exists — + ISecurityEventRepository, IAppSettingRepository
│   ├── Common/Abstractions/         ISessionCookieWriter, ICurrentRequestContext (NEW)
│   └── Features/
│       ├── Auth/Commands/           Register, ResendOtp, VerifyOtp, Login, Refresh,
│       │                            Logout, ForgotPassword, ResetPassword, ChangePassword
│       ├── Sessions/Queries+Commands/  GetActiveSessions, RevokeSession, RevokeAllOtherSessions
│       ├── Security/Commands/       ChangePassword, SuspendUser
│       └── Admin/Roles+Users/       UpdateRolePermissions, SetUserPermissionOverride, ElevateUserRole
├── CsRaiders.Api.Infrastructure/          # security services + persistence (new content)
│   ├── Security/    Argon2idPasswordHasher, HmacOtpGenerator, TokenService (NEW)
│   ├── Services/    LoginAttemptTracker, SessionReconciliationService,
│   │                SecurityAuditWriter, ChannelEmailQueue, EmailSenderWorker,
│   │                AuthCleanupWorker (NEW)
│   ├── Interceptors/  AuditSaveChangesInterceptor [MODIFY: column-name redaction]
│   └── Persistence/  AppDbContext [MODIFY], Configurations/SecurityEvent+AppSetting configs
│                      + Migrations/ (delta: login_attempts key, pepper_version, security_events, app_settings)
└── CsRaiders.Api.API/
    ├── Middleware/  SessionValidationMiddleware (NEW)
    ├── Authorization/  AuthPolicies, HasPermissionAttribute, PermissionAuthorizationHandler (NEW)
    ├── Filters/     OriginAndHeaderValidationFilter (NEW)
    ├── Controllers/ AuthController, MeSessionsController (NEW); AdminRolesController, AdminUsersController (NEW)
    └── Program.cs   [MODIFY: forwarded headers first, rate limiter, authn/authz, cookies, entropy checks, workers]

tests/
├── CsRaiders.Api.Domain.UnitTests/        # exists — SessionPolicyTests, EffectivePermissionsTests, etc.
├── CsRaiders.Api.ArchitectureTests/       # NEW (solution: layering + authorization coverage rules)
└── CsRaiders.Api.IntegrationTests/        # NEW (xUnit + Testcontainers; SEC-01…SEC-18)
```

**Structure Decision**: Extend the existing four-project layered solution exactly as laid out above; no new production projects, two new test projects. All new HTTP behavior lives under `/api/v1/auth`, `/api/v1/me/sessions`, and `/api/v1/admin/*` as contractually declared in `contracts/openapi.yaml`.

## Complexity Tracking

> Constitution is an unratified template — no formal violations table required. The documented complexity justifications live in `research.md` (Alternatives Considered in the design: no Redis, no outbox, no double-submit CSRF, permission-not-role authorization).
