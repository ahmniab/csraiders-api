# Contract: Token Refresh API (`POST /api/v1/auth/refresh`)

**Contract Type**: REST API Endpoint Specification  
**Version**: 1.0.0  
**Feature**: `002-token-bitmask-authorization`  

---

## 1. Overview

The refresh endpoint exchanges an active refresh token cookie for a replacement 5-minute access token (containing the `perms` claim) and a rotated refresh token cookie.

- **URL**: `/api/v1/auth/refresh`
- **Method**: `POST`
- **Authentication**: Cookie-based (`__Host-csraiders_rt`)
- **Rate Limit**: 100 requests per minute per IP partition (`refresh_{ip}`)

---

## 2. Request Specification

### Headers
| Header | Type | Required | Description |
|:---|:---|:---|:---|
| `X-Device-Fingerprint` | `string` | Yes | Device fingerprint identifier (max 128 chars). Must match the active session. |
| `Content-Type` | `string` | No | `application/json` (body is typically empty or optional). |

### Cookies
| Name | Required | Attributes | Description |
|:---|:---|:---|:---|
| `__Host-csraiders_rt` | Yes | `HttpOnly; Secure; SameSite=Strict; Path=/` | Signed JWT refresh token. |

---

## 3. Response Specification

### 3.1 HTTP 200 OK (Success)

Issued upon successful token validation and session rotation.

#### Response Headers
- `Set-Cookie`: `__Host-csraiders_rt=<new_refresh_token>; Path=/; Secure; HttpOnly; SameSite=Strict; Max-Age=2592000`

#### Response Body Schema
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "sessionId": "7b8c4f10-9b48-4f6e-9b48-382a9db261e4"
}
```

- `accessToken`: Signed 5-minute JWT containing the `perms` decimal string claim.
- `sessionId`: UUID matching the active session.

---

### 3.2 HTTP 401 Unauthorized (Failure Modes)

Returned when token validation or session rotation fails. Returns RFC 7807 Problem Details.

#### Failure Codes:
- `invalid_grant`: Refresh token signature invalid, expired, missing cookie, or user does not match session.
- `session_superseded`: Device fingerprint mismatch or session inactive/expired.
- `session_compromised`: Replayed refresh token hash detected. Session is immediately terminated and security event is logged.

#### Example Response Body:
```json
{
  "error": {
    "code": "invalid_grant",
    "message": "The refresh token is invalid or expired."
  }
}
```

---

### 3.3 HTTP 429 Too Many Requests (Rate Limited)

Returned when requests from a single IP exceed 100 requests within a 1-minute fixed window.

#### Response Headers:
- `Retry-After`: Seconds until the current fixed rate-limit window resets.

#### Response Body:
```json
{
  "error": {
    "code": "rate_limited",
    "message": "Too many refresh attempts. Please try again later."
  }
}
```

---

## 4. Rate Limiting Partitioning Contract

| Endpoint Pattern | Partition Key Format | Window | Limit | Target Use Case |
|:---|:---|:---|:---|:---|
| `POST /api/v1/auth/refresh` | `refresh_{ip}` | 1 minute | **100 requests** | High-frequency token refresh from corporate/campus NATs. |
| `POST /api/v1/auth/*` (other) | `auth_{ip}` | 1 minute | **20 requests** | Brute-force protection for login, register, OTP verification. |
| Non-auth endpoints | `unlimited` | N/A | No limiter | Protected business endpoints. |
