# Cross-cutting HTTP contract: cookies, headers, error taxonomy

Feature: `001-auth-session-management` · complements `openapi.yaml`

## 1. Cookie contract

| Name | Attributes | Set at | Cleared at |
|---|---|---|---|
| `__Host-csraiders_rt` | `Secure; HttpOnly; SameSite=Strict; Path=/`; host-prefixed name; 30-day Max-Age | login, refresh (rotation), verify-otp (none — login only) | logout, refresh failure (invalid_grant / superseded / compromised) |
| `csraiders_dfp` | `Secure; HttpOnly; SameSite=Strict; Path=/`; 1-year Max-Age; opaque random value ≠ any user data | login (only when absent) | never (persistent) |

Rules:
- API must set cookies with the `__Host-` prefix only over HTTPS and never with a `Domain` attribute (prefix requirement).
- The device fingerprint value (`csraiders_dfp`) must be server-generated randomness; the API rejects a changed/missing value at refresh with `401 session_superseded` + cookie clearing (device mismatch).
- No CSRF cookie exists (double-submit deliberately not adopted).

## 2. Required headers on cookie-acting endpoints

`/auth/refresh` and `/auth/logout` (only endpoints that act on cookies) require all of:

1. `Origin` equals configured `Site:Origin` (fall back to `Referer`; missing both ⇒ `403`).
2. `X-Requested-With: csraiders` (cross-site pages cannot silently attach without preflight).
3. `Content-Type: application/json`.

All other endpoints carry `Authorization: Bearer <access>` which browsers never auto-attach.

## 3. Error taxonomy (stable codes)

Every body: `{ "error": { "code": "...", "message": "<human text>" } }` (messages never echo credential internals).

| Code | HTTP | Meaning / notes |
|---|---|---|
| `invalid_credentials` | 401 | Unknown email, wrong password, suspended account, AND gate-throttled/locked attempts — one uniform code (no 423 anywhere). `Retry-After` header present whenever a delay/block is active (D8). |
| `session_superseded` | 401 | Session revoked/expired/idle-expired/device-mismatch; cookies cleared; includes `logout`/`password_*`/`policy_reduced`/`cap_exceeded`/`admin_revoked` downstream reasons (not enumerated to client further). |
| `session_compromised` | 401 | Retired refresh token replayed after the 15 s grace window; session revoked (`reuse_detected`); cookies cleared; write must survive rollback. |
| `invalid_grant` | 401 | Refresh request without a recognizable cookie token. |
| `invalid_or_expired_otp` | 400 | Verify-otp generic failure (wrong/expired/attempt-exhausted are deliberately indistinguishable). |
| `forbidden` | 403 | Failed origin/header validation, or missing effective permission on a `[HasPermission]` endpoint. |
| `validation_failed` | 422 | Structural validation (email format, password length/ NFC, name length). |
| `already_registered` | 409 | Registration was attempted for an email that already has an account; no notification email is queued. |
| `rate_limited` | 429 | IP limiter (20/min/IP on `/auth/*`); `Retry-After` set. |
| `capacity_exhausted` | 503 | Password-hashing semaphore wait timeout (2 s) — shed load, never queue forever. |

## 4. Success mail rules

- Register / resend / forgot-password return the constant `202` body (`AuthAcceptedResponse`) with byte-identical timing envelope regardless of account state.
- Registry of security-relevant outcomes: suspension, elevation, already-registered registration attempts, and password reset token issuance.

## 5. Observability surface (from FR-038)

| Signal | Type | Notes |
|---|---|---|
| `auth_login_attempts_total{result=ok,blocked,throttled}` | counter | per gate outcome |
| `auth_otp_failures_total` / `auth_otp_sends_total{purpose}` | counter | resend/issuance counts |
| `auth_token_reuse_detected_total` | counter | every `session_compromised` |
| `auth_sessions_revoked_total{reason}` | counter | values of the revoke-reason enum |
| `email_queue_dropped_total` / `email_queue_retries_total{attempt}` | counter | queue-full and retry warnings |
| `auth_session_cache_hits_total` / `..._misses_total` | counter | ≥95% hit-rate target (SC-007) |

All counters and logs honor the redaction deny-list (`password`, `otp`, `refresh_token`, `token_hash`, `password_hash`, `otp_hash`, `refresh_token_hash`, `previous_refresh_token_hash`).

## 6. Startup contract (fail-fast)

On boot the API refuses to start unless: pepper/HMAC/signing keys meet minimum entropy; `Site:Origin` present (production); DB migrations applied; system role presets seeded (`IdentitySeeder`). In development the migration + seeding run on startup (existing behavior). No session cache prepopulation is required.
