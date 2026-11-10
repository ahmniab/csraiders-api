# Hybrid JWT Refresh Token Design

Status: Draft for review  
Scope: Authentication refresh flow (`POST /api/v1/auth/refresh`)

## 1. Summary

Refresh tokens will be signed JWTs containing a session identifier, while the database remains authoritative for session state, rotation, revocation, device binding, and replay detection.

The JWT provides efficient routing to the session row by `sid`. It does not grant refresh access by itself. A refresh succeeds only when both the JWT and the locked database session state are valid.

```text
refresh cookie
    ↓
validate JWT signature and lifetime
    ↓
extract sid
    ↓
SELECT session WHERE id = sid FOR UPDATE
    ↓
hash presented JWT and compare with current/previous hash
    ↓
apply rotation, grace, device, and revocation rules
```

## 2. JWT claims

The refresh JWT should contain only claims needed for validation and lookup:

```json
{
  "iss": "configured Site:Origin",
  "aud": "configured Site:Origin",
  "sub": "user-id",
  "sid": "session-id",
  "jti": "unique-token-id",
  "iat": 1730000000,
  "exp": 1732592000
}
```

Required rules:

- Algorithm is pinned to HS256, matching the access-token policy.
- `sid`, `sub`, and `jti` must be valid GUIDs.
- No roles or permissions are placed in the refresh JWT.
- The JWT is stored only in the `__Host-csraiders_rt` secure, HTTP-only, SameSite=Strict cookie.
- The database stores a SHA-256 hash of the complete JWT, never the raw token.
- The JWT lifetime must not exceed the session’s 30-day absolute lifetime.

## 3. Session creation

The session ID must exist before the refresh JWT is generated.

Recommended sequence:

1. Generate a session ID and refresh `jti`.
2. Create the refresh JWT using the session ID and user ID.
3. Hash the complete JWT.
4. Create the `Session` using that hash.
5. Persist the session and return the JWT in the refresh cookie.

`Session.Open` can be extended to accept an explicit session ID, or a factory can create the session identity before calling the existing entity method.

## 4. Refresh request flow

### 4.1 Input validation

The endpoint first validates the request headers and cookies:

- Origin or Referer matches `Site:Origin`.
- `X-Requested-With: csraiders` is present.
- Content type is `application/json`.
- Refresh cookie exists.
- Device fingerprint cookie exists.

Missing refresh cookie returns `401 invalid_grant`. Missing device fingerprint returns `401 session_superseded` and clears authentication cookies.

### 4.2 JWT validation

The token service validates:

- Signature and configured signing key.
- Algorithm allow-list.
- Issuer and audience.
- `iat` and `exp`.
- Required `sid`, `sub`, and `jti` claims.

Any failure returns `401 invalid_grant` and clears the refresh cookie. The database must not be queried when the JWT is structurally invalid or fails cryptographic validation.

### 4.3 Locked session lookup

After JWT validation:

```sql
SELECT ...
FROM sessions
WHERE id = @sid
FOR UPDATE;
```

The lookup is by primary key. The transaction must remain open until the rotation decision and session update are persisted.

The handler must also verify that the session’s `UserId` matches the JWT `sub`. A mismatch is treated as `invalid_grant`.

### 4.4 Token state verification

The handler hashes the complete presented JWT and compares it with:

- `RefreshTokenHash`, or
- `PreviousRefreshTokenHash`.

Comparison must use constant-time equality. The JWT’s signature and `jti` are not sufficient because a correctly signed but already-rotated token must still be rejected or handled by the grace rule.

## 5. Rotation and replay rules

### Current token match

If the presented hash matches `RefreshTokenHash`:

1. Generate a new `jti`.
2. Generate a new JWT with the same `sid` and `sub`.
3. Move the current hash to `PreviousRefreshTokenHash`.
4. Store the new JWT hash as `RefreshTokenHash`.
5. Set `RotatedAt = now`.
6. Extend the idle expiration, without exceeding the absolute expiration.
7. Commit and return the new access token and refresh cookie.

### Previous token within grace

If the presented hash matches `PreviousRefreshTokenHash` and `now - RotatedAt <= 15 seconds`, treat it as a benign retry:

- Rotate again.
- Return another valid refresh JWT.
- Keep the session active.

This supports concurrent requests or a lost response during rotation.

### Previous token after grace

If the presented hash matches `PreviousRefreshTokenHash` and the grace window has elapsed:

1. Revoke the session with `reuse_detected`.
2. Persist the revocation in the same transaction.
3. Commit even though the response is an error.
4. Evict the session cache after commit.
5. Record `token_reuse_detected` through the security audit path.
6. Return `401 session_compromised`.

### No hash match

Return `401 invalid_grant` and clear the refresh cookie. Do not revoke the session because the token may simply belong to another session or be malformed.

## 6. Device validation

The session’s stored device fingerprint must exactly match the `csraiders_dfp` cookie.

If it does not match:

- Do not rotate the token.
- Do not classify it as token reuse.
- Return `401 session_superseded`.
- Clear the refresh and device cookies.

This check must occur while the session row is locked and before changing token state.

## 7. Failure mapping

| Condition | HTTP | Code | Session action |
|---|---:|---|---|
| Missing refresh cookie | 401 | `invalid_grant` | None |
| Invalid signature/claims/expiry | 401 | `invalid_grant` | None |
| Unknown `sid` | 401 | `invalid_grant` | None |
| JWT `sub` differs from session owner | 401 | `invalid_grant` | None |
| Session revoked or expired | 401 | `session_superseded` | None |
| Device fingerprint mismatch | 401 | `session_superseded` | None |
| Previous token within 15 seconds | 200 | — | Rotate again |
| Previous token after 15 seconds | 401 | `session_compromised` | Revoke with `reuse_detected` |

All failure paths clear the refresh cookie. Superseded, compromised, and device-mismatch paths also clear the device cookie.

## 8. Data model impact

No new session columns are required if the existing fields remain hash-based:

- `refresh_token_hash`
- `previous_refresh_token_hash`
- `rotated_at`
- `revoked_at`
- `revoke_reason`

The `jti` does not need a separate column because the complete JWT hash is already sufficient to identify the exact generation. If operational diagnostics require it, a non-secret `current_refresh_jti` and `previous_refresh_jti` may be added, but they must not replace hash verification.

## 9. Security considerations

- A valid JWT alone never bypasses database session checks.
- Session revocation remains immediate because every refresh consults the database.
- JWT signing-key rotation invalidates all refresh JWTs signed by the retired key unless a key-ID rotation strategy is introduced.
- Refresh JWTs are larger than opaque random tokens and should remain cookie-only.
- Claims must not include roles, permissions, password state, or other mutable authorization data.
- The raw refresh JWT must never appear in logs, audit details, exceptions, or metrics.
- The session lookup must use the validated `sid`, not an untrusted cookie value without JWT verification.

## 10. Performance expectation

The expected lookup changes from an indexed search on current/previous token hashes to a primary-key lookup by `sid`. Both should be indexed, but the primary-key path is simpler and generally cheaper.

Before and after implementation, compare:

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT ... FROM sessions WHERE id = '...';
```

against the existing hash lookup. The performance change should be measured under realistic session-row counts rather than assumed from query shape alone.

## 11. Migration plan

1. Add refresh-JWT creation and validation behind the existing `ITokenService` abstraction.
2. Add a feature/configuration switch for accepting hybrid refresh tokens.
3. Continue accepting existing opaque refresh tokens during a short migration window, if required.
4. On successful opaque refresh, issue a hybrid JWT and replace the cookie.
5. Remove opaque-token compatibility after all active sessions have naturally rotated or been invalidated.
6. Keep the database hash fields and replay logic unchanged.

## 12. Open review questions

1. Should the hybrid refresh JWT use the same signing key as access tokens, or a dedicated refresh signing key?
2. Should the JWT `sub` be retained, or should only `sid` and `jti` be included to minimize claims?
3. Do we need temporary support for existing opaque refresh cookies during deployment?
4. Should refresh JWTs use a `kid` header to support signing-key rotation without logging out all sessions?
