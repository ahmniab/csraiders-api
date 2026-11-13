# Stage 7 Critic Review Report

**Target:** `docs/designs/token_embedded_authorization_bitmask_claims.md` (`lines 1-297`)  
**Standard:** `resources/document_standards/feature-spec-standard.md`  
**Report Destination:** `critic_report.md`

---

## Reviewer Status

| Reviewer | Status |
| --- | --- |
| `egm-design` | completed |
| `egm-structural` | completed |
| `egm-style` | completed |

## Severity Breakdown
* 🔴 **Blocker**: 1
* 🟡 **Major**: 2
* 🟢 **Nit**: 4

---

## Prioritized Findings (Blockers -> Major -> Nits)

### [Ambient Transaction Incompatibility & Unverified Database Query Amplification in Token Refresh] - Severity: Blocker
* **Reviewer Lens:** `egm-design`
* **Affected Scope:** `Lines 62, 98–101, 219–228` (`CUJ 3`, `Technical Plan §1`, `Technical Plan §5`)
* **Discrepancy Description**:  
  The plan mandates "Pre-Lock Resolution" where `RefreshTokenCommandHandler` queries effective permissions from `users.FindByIdWithPermissionsAsync` and `roles.FindByIdsAsync` *prior* to acquiring the `sessions.FindByIdForUpdateAsync` pessimistic row lock, asserting it will "commit transaction (< 2 ms)". This introduces two critical defects:
  1. **Framework Lifecycle Violation**: MediatR's ambient `TransactionBehavior<TMessage, TResponse>` controls database transactions at the pipeline boundary (`transactions.BeginAsync` before handler execution and `transaction.CommitAsync` after). Handlers cannot manually commit transactions without throwing runtime exceptions.
  2. **Unauthenticated Query Amplification DoS**: Querying `users` and `roles` before verifying session existence (`session is null`), ownership (`session.UserId != claims.UserId`), and rotation validity (`session.TryRotate(...)`) allows callers presenting expired, forged, revoked, or replayed refresh tokens to force PostgreSQL multi-table joins without a valid session.
* **Rationale & Recommendation**:  
  Reorder execution to fail fast on invalid sessions before hitting permission repositories:
  1. Verify the refresh token cryptographic signature and expiration via `tokens.TryValidateRefreshToken`.
  2. Acquire the session row lock via `sessions.FindByIdForUpdateAsync(claims.SessionId, ct)`.
  3. Validate ownership and execute rotation via `session.TryRotate(...)`.
  4. Only on `RefreshOutcome.Rotated`, resolve effective permissions from `users` and `roles`, compute the bitmask via `PermissionBitmaskMapper.ToBitmask`, and issue the token. (The row lock targets only the specific session row, so holding it across permission queries does not contend with other users).
  5. Remove all claims of manual handler commits and allow ambient `TransactionBehavior` to commit the transaction atomically.

---

### [Administrative Privilege Revocation Propagation Gap] - Severity: Major
* **Reviewer Lens:** `egm-design`
* **Affected Scope:** `Lines 31, 37, 268–272` (`Goals`, `Non-Goals`, `Non-Functional Requirements §Security & Privacy`)
* **Discrepancy Description**:  
  The specification accepts a 5-minute propagation delay (+ 30s clock skew tolerance) for administrative permission revocations and role modifications. However, when an administrator revokes critical capabilities (`payment:refund`, `role:manage`, `user:write`, `session:revoke`) or demotes a compromised user, the affected client retains active administrative privileges on their 5-minute JWT access token until expiration. While `SessionValidationMiddleware.cs` enforces 5-second revocation for explicit session terminations, the document does not require permission/role revocation commands to invalidate active sessions.
* **Rationale & Recommendation**:  
  Explicitly mandate in the security specifications that commands modifying or revoking sensitive roles or permissions (e.g. in `RbacCommands.cs`) must invoke `ISessionRepository.RevokeAllByUserIdAsync(targetUserId)` to force immediate 5-second termination via `SessionValidationMiddleware.cs`.

---

### [Structural Heading Hierarchy Violation for Trailing Sections] - Severity: Major
* **Reviewer Lens:** `egm-structural`
* **Affected Scope:** `Lines 285–295` (`### Additional Resources`, `### Future Improvements`)
* **Discrepancy Description**:  
  Following the horizontal divider at Line 285 that concludes `## Non-Functional Requirements`, `Additional Resources` (Line 286) and `Future Improvements` (Line 293) are rendered as H3 (`###`) subsections rather than top-level H2 (`##`) sections. This structurally orphans them under `## Non-Functional Requirements`, violating CommonMark structural standards and corrupting table-of-contents generation.
* **Rationale & Recommendation**:  
  Promote both sections to top-level H2 headings:
  - `## Additional Resources`
  - `## Future Improvements`

---

### [Physically Impossible Sub-Nanosecond Latency Assertions] - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 13, 52, 94, 215, 277` (`Overview`, `CUJ 1`, `Technical Plan §1`, `Technical Plan §4`, `Performance & Latency`)
* **Discrepancy Description**:  
  The specification repeatedly asserts that `PermissionAuthorizationHandler` evaluates authorization in `< 1 nanosecond`. In ASP.NET Core, evaluating `context.User.FindFirst("perms")`, parsing `long.TryParse`, resolving `PermissionBitmaskMapper.GetBit`, and calling `context.Succeed(requirement)` incurs claim iteration, string scanning, and method dispatch overhead typically taking ~20–50 nanoseconds (sub-microsecond). Claiming sub-nanosecond latency for the entire handler pipeline is technically unrealistic.
* **Rationale & Recommendation**:  
  Calibrate the claims to "sub-microsecond" (or "~20–50 ns") for the authorization handler execution, noting that only the isolated bitwise instruction (`(bitmask & requiredBit) != 0`) executes in sub-nanoseconds.

---

### [Redundant Author Prefixes in Alternatives Considered] - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 246, 250, 255, 260` (`Alternatives Considered`)
* **Discrepancy Description**:  
  Every alternative considered contains repetitive boilerplate text: `* **Reason for Rejection**: Rejected by author: [explanation]`.
* **Rationale & Recommendation**:  
  Streamline the entry to `* **Reason for Rejection**: [explanation]`.

---

### [Plain Code-Tick File Paths in Resource References] - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 287–290` (`Additional Resources`)
* **Discrepancy Description**:  
  File paths in `Additional Resources` are formatted as plain monospace text rather than clickable markdown links with `file:///` URIs.
* **Rationale & Recommendation**:  
  Format each path as a clickable markdown link using the `file:///` scheme (e.g. [`PermissionCatalog.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Constants/PermissionCatalog.cs)).

---

### [Asymmetric Horizontal Dividers Across Document Sections] - Severity: Nit
* **Reviewer Lens:** `egm-structural`
* **Affected Scope:** `Lines 7, 69, 238, 262, 285, 296`
* **Discrepancy Description**:  
  Horizontal dividers (80-character dashes) are placed inconsistently: one appears after the metadata header, but sections `## Overview`, `## The Problem`, `## Goals`, `## Non-Goals`, `## Open Questions`, and `## Critical User Journeys` lack dividers. Dividers then reappear before `## Technical Plan`, `## Alternatives Considered`, `## Non-Functional Requirements`, etc.
* **Rationale & Recommendation**:  
  Apply horizontal dividers consistently between all top-level `##` sections or standardize on clean whitespace separation without arbitrary 80-character dash lines.
