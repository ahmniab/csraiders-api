# Contract: JWT Access Token Claims & Client Bitmask Evaluation

**Contract Type**: Token Claims Specification & Client Interface  
**Version**: 1.0.0  
**Feature**: `002-token-bitmask-authorization`  

---

## 1. Access Token Payload Specification

Access tokens issued by `CsRaiders.Api` are cryptographically signed JSON Web Tokens (JWT) adhering to RFC 7519 using HMAC-SHA256 (`HS256`).

### JSON Payload Schema

```json
{
  "sub": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sid": "7b8c4f10-9b48-4f6e-9b48-382a9db261e4",
  "perms": "1048575",
  "nbf": 1794508800,
  "exp": 1794509100,
  "iat": 1794508800,
  "iss": "https://api.csraiders.com",
  "aud": "https://api.csraiders.com"
}
```

### Claim Definitions

| Claim | Type | Required | Description |
|:---|:---|:---|:---|
| `sub` | `string` (UUID) | Yes | Unique identifier of the authenticated user. |
| `sid` | `string` (UUID) | Yes | Identifier of the authenticated session associated with this token. |
| `perms` | `string` (ASCII digits) | Yes | Effective permission capability bitmask represented as an invariant unsigned base-10 decimal integer string. |
| `iat` | `number` (Epoch seconds) | Yes | Issuance timestamp. |
| `exp` | `number` (Epoch seconds) | Yes | Expiration timestamp ($iat + 300\text{ seconds}$). |
| `iss` | `string` (URI) | Yes | Issuer authority matching `Site:Origin`. |
| `aud` | `string` (URI) | Yes | Audience matching `Site:Origin`. |

---

## 2. Invariant Claim Format Rules

1. **Decimal String Encoding**:
   The `perms` claim MUST always be encoded as a string of decimal digits (`"0"`, `"1"`, `"1048575"`). It MUST NOT be emitted as a bare JSON number to prevent JavaScript 53-bit floating-point truncation.
2. **Value Range**:
   The value represents a 64-bit unsigned integer in the inclusive range:
   $$0 \le \text{perms} \le 18,446,744,073,709,551,615 \quad (2^{64}-1)$$
3. **No Padding or Prefixes**:
   No leading zeros (except for `"0"` itself), no whitespace, no signs (`+` or `-`), and no hexadecimal prefixes (`0x`).

---

## 3. Client-Side Evaluation Specification (JavaScript / TypeScript)

Client applications (SPAs, mobile applications) MUST decode and evaluate the `perms` claim using arbitrary-precision integers (`BigInt`).

### Catalog Bit Constants Reference

```typescript
export const PermissionBits = {
  CourseRead:       1n << 0n,   // course:read
  CourseWrite:      1n << 1n,   // course:write
  CoursePurge:      1n << 2n,   // course:purge
  LessonWrite:      1n << 3n,   // lesson:write
  AssetWrite:       1n << 4n,   // asset:write
  QuestionWrite:    1n << 5n,   // question:write
  QuizWrite:        1n << 6n,   // quiz:write
  UserRead:         1n << 7n,   // user:read
  UserWrite:        1n << 8n,   // user:write
  RoleManage:       1n << 9n,   // role:manage
  PaymentRead:      1n << 10n,  // payment:read
  PaymentVerify:    1n << 11n,  // payment:verify
  PaymentRefund:    1n << 12n,  // payment:refund
  NotificationSend: 1n << 13n,  // notification:send
  AnalyticsRead:    1n << 14n,  // analytics:read
  AuditRead:        1n << 15n,  // audit:read
  SettingsRead:     1n << 16n,  // settings:read
  SettingsWrite:    1n << 17n,  // settings:write
  SessionMulti:     1n << 18n,  // session:multi
  SessionRevoke:    1n << 19n   // session:revoke
} as const;
```

### Evaluation Logic

```typescript
/**
 * Evaluates whether an access token's perms claim grants the required permission.
 * @param permsClaim - The decimal integer string from the decoded JWT payload.
 * @param requiredBit - The BigInt bitmask corresponding to the permission.
 * @returns True if granted, false otherwise.
 */
export function hasPermission(permsClaim: string | undefined | null, requiredBit: bigint): boolean {
  if (!permsClaim || !/^\d+$/.test(permsClaim)) {
    return false;
  }
  try {
    const bitmask = BigInt(permsClaim);
    return (bitmask & requiredBit) !== 0n;
  } catch {
    return false;
  }
}
```

### Test Equivalence Guarantee
A client evaluating permissions using `BigInt` will produce the exact same boolean allow/deny outcome as the server's `ulong` evaluation for all 64 potential positions.
