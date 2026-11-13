# Contract: Permission Authorization Policy (`[HasPermission]`)

**Contract Type**: Internal Authorization Policy & HTTP Behavior Contract  
**Version**: 1.0.0  
**Feature**: `002-token-bitmask-authorization`  

---

## 1. Overview

Endpoints in `CsRaiders.Api` protecting business capabilities are decorated with `[HasPermission("<permission_string>")]` (e.g. `[HasPermission("course:read")]`).

The authorization middleware evaluates permissions strictly in memory using the `perms` bitmask claim in the caller's JWT access token.

---

## 2. Policy Resolution Pipeline

```text
Incoming HTTP Request
   │
   ▼
[JwtBearerHandler]
   │  - Validates JWT signature, lifetime (5m), audience, issuer
   │  - Populates ClaimsPrincipal (sub, sid, perms)
   │  - On invalid signature or expired: HTTP 401 Unauthorized
   ▼
[SessionValidationMiddleware]
   │  - Validates active session via 'sid' claim
   │  - Checks revocation, idle timeout, single-session constraints
   │  - Cached for 5s; on revoked/expired session: HTTP 401 Unauthorized
   ▼
[PermissionAuthorizationHandler]
   │  - Extracts 'perms' claim
   │  - If 'perms' missing, empty, or unparseable: context.Fail() -> HTTP 403 Forbidden
   │  - Computes requiredBit = PermissionBitmaskMapper.GetBit(requirement.Permission)
   │  - If requiredBit == 0UL: context.Fail() -> HTTP 403 Forbidden
   │  - If (bitmask & requiredBit) != 0UL: context.Succeed()
   │  - Else: context.Fail() -> HTTP 403 Forbidden
   ▼
[Controller Action] (HTTP 200 OK / Business Logic)
```

---

## 3. HTTP Status Codes & Error Behavior

| Scenario | HTTP Status Code | Response Body | Fallback to DB/Cache? |
|:---|:---|:---|:---|
| Valid token, `perms` contains required bit | `200 OK` (or endpoint result) | Business response | **No** (pure bitwise check) |
| Valid token, `perms` lacks required bit | `403 Forbidden` | Empty or ASP.NET default forbidden | **No** |
| Valid token, `perms` claim is missing | `403 Forbidden` | Immediate rejection | **No** |
| Valid token, `perms` is negative or non-numeric | `403 Forbidden` | Immediate rejection | **No** |
| Valid token, unknown permission requested | `403 Forbidden` | Immediate rejection | **No** |
| Token expired (> 5 min) or invalid signature | `401 Unauthorized` | JWT Bearer challenge | **No** |
| Session revoked via `ISessionRepository.RevokeAllByUserIdAsync` | `401 Unauthorized` | Problem Details (`session_superseded`) within 5s | Intercepted by `SessionValidationMiddleware` |

---

## 4. Latency & Resource Commitments

- **Evaluation Time**: $< 50$ nanoseconds (CPU bitwise AND).
- **Allocations**: 0 heap bytes per authorization check on the hot path.
- **I/O Operations**: 0 database queries, 0 cache queries.
