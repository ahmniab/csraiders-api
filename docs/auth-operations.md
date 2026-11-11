# Authentication Operations

## Required startup configuration

Production startup requires high-entropy values for:

- `Auth:PasswordPepper:<version>` (at least 32 characters)
- `Auth:OtpHmacKey` (at least 32 characters)
- `Auth:JwtSigningKey` (at least 32 characters)
- `Site:Origin`

Development secrets should be stored with `dotnet user-secrets`, never committed to
`appsettings.json`. The API expects HTTPS in normal operation. When deployed behind
nginx, nginx must overwrite `X-Forwarded-For` and `X-Forwarded-Proto`; only configured
`Proxy:KnownProxies` addresses are trusted.

## Cookie and request contract

Login issues the `__Host-csraiders_rt` refresh cookie and the `csraiders_dfp` device
fingerprint cookie. Both are `Secure`, `HttpOnly`, `SameSite=Strict`, and scoped to
`Path=/`. Refresh and logout additionally require:

- `Origin` matching `Site:Origin` (or a matching `Referer` fallback)
- `X-Requested-With: csraiders`
- `Content-Type: application/json`

Access tokens contain only `sub`, `sid`, `iat`, and `exp`; permissions are always
resolved server-side.

## Retention and cleanup

The hourly `AuthCleanupWorker` removes revoked/expired sessions older than 30 days,
expired pending registrations, reset tokens older than seven days, inactive login
attempts older than 24 hours, and security events older than 30 days. Cleanup is
serialized per process and logs deletion counts.

## RBAC and sessions

Permission changes cancel the global permission-cache token, so deny-wins changes are
visible on the next authorization check rather than waiting for the 30-second cache
TTL. Removing `session:multi` revokes older active sessions with reason
`policy_reduced` and leaves the newest session in single-session mode.

## Deployment topology

The permission/session caches and bounded email queue are process-local. The supported
topology is one API process per authoritative PostgreSQL instance. If multiple API
instances are introduced, move cache invalidation, session status, and email delivery
to shared infrastructure before relying on cross-instance revocation guarantees.
