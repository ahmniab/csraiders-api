# Quickstart: validate the Authentication & Session feature end-to-end

Feature: `001-auth-session-management`

Prerequisites graph: [plan.md](plan.md) → [research.md](research.md) (D1–D16) → [data-model.md](data-model.md) → [contracts/openapi.yaml](contracts/openapi.yaml) + [contracts/errors-and-cookies.md](contracts/errors-and-cookies.md). This file is a validation checklist — no implementation bodies.

---

## 1. Prerequisites

- .NET SDK 10 (pinned by `global.json`), Docker (Testcontainers), PostgreSQL 16 reachable or containerized
- Development secrets (`dotnet user-secrets` — dev only), meet the startup entropy gate:
  - `Auth:PasswordPepper:1` (the active pepper version), `Auth:OtpHmacKey`, `Auth:JwtSigningKey`, `Site:Origin`
- Local HTTPS: mkcert certificate trusted for `localhost` (research D16); `ASPNETCORE_ENVIRONMENT=Development` triggers migrate+seed on boot (existing behavior)

## 2. Build & run

```bash
dotnet build                             # 4 production projects + tests compile clean
dotnet run --project src/CsRaiders.Api.API
```

Expected: no startup gate failures; Swagger available under `/swagger` (Development).

Smoke test (from `contracts/openapi.yaml`):

```bash
curl -X POST https://localhost:5001/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"student@example.com","name":"Student","password":"Sup3rSecret!"}'
# → 202, constant body; then likewise register the same email → identical 202 body & sub-second time
```

## 3. Scenarios — the spec's acceptance criteria, runnable

Map each scenario to its contract / data-model anchor:

| # | Scenario → | Anchor |
|---|---|---|
| 1 | register → verify-otp (code from email outbox/log) → login works; wrong codes ×5 then correct ⇒ 400 invalid_or_expired_otp | US1; FR-001..FR-005, SEC-03 |
| 2 | resend-otp: 60 s cooldown honored, old code no longer valid, new code verifies; re-register same pending email never changes stored hash (DB inspection) | US1/FR-004/FR-005, SEC-15/16 |
| 3 | login from 2nd device supersedes 1st: device A's next call 401 session_superseded within ≤5 s; 10 concurrent logins leave exactly 1 active row | US3, SEC-05/SEC-10 |
| 4 | admin (elevated role via admin endpoints) with session:multi logs in 4× ⇒ oldest revoked cap_exceeded at 4th (cap 3); revoke that permission ⇒ 2× policy_reduced + survivor single-session | US7, SEC-06/07 |
| 5 | refresh: rotate succeeds; replay old token ≤15 s later rotates (SEC-11); replay after grace ⇒ 401 session_compromised and session row revoked reuse_detected — even when surrounding borrow tx in test fails around an exception | US4, SEC-08 |
| 6 | failed logins: 4–9 failures → Retry-After grows (≤15 s); 10th ⇒ 15-min block and still 401 (not 423); same email different IP logs in fine | US2, SEC-04 |
| 7 | RBAC flips: change role/override → next request (not re-login) reflects within 30 s; deny-wins validated | US7, SEC-18 |
| 8 | forgot/reset: uniform 202 any email; single-use 30-min token; all sessions revoked password_reset | US6 |
| 9 | suspension: all sessions revoked immediately; login is exactly the invalid_credentials 401 | US8, SEC-17 |
| 10 | session list/revoke-others: only own sessions; revoked device dies | US5 |
| 11 | secrets redaction sweep: grep logs + security_events payloads for the deny-list fields in contracts §3 | SC-008 |
| 12 | spoofed `X-Forwarded-For` ignored; refresh/logout missing Origin or X-Requested-With = 403 | SEC-12/14 |

## 4. Automated suites

```bash
dotnet test tests/CsRaiders.Api.Domain.UnitTests         # policy, OTP, hashing-shape, time math
dotnet test tests/CsRaiders.Api.ArchitectureTests        # layering + authorization coverage (research D14)
dotnet test tests/CsRaiders.Api.IntegrationTests         # Testcontainers; SEC-01..SEC-18; https://localhost base
```

Expected: all green on a healthy run; integration suite provisions a throwaway PostgreSQL container; no external Redis/broker ever required anywhere.

## 5. Acceptance gates (must hold before /speckit.tasks sign-off)

1. Every SEC from research/verification table above passes (18/18).
2. Load sanity: sustained mixed traffic shows ≥95% cache-hit ratio on `auth_session_cache_*` counters while revocation latency stays ≤5 s worst-case.
3. `grep` redaction sweep over logs + audit rows returns zero leaks.
4. No `423`, no role-based attributes, no roles-in-JWT findings from architecture tests.
