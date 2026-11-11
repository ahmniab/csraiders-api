# Stage 7 Critic Review Report (Re-Audit)

* **Document:** [`docs/designs/token_embedded_authorization_bitmask_claims.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/docs/designs/token_embedded_authorization_bitmask_claims.md)
* **Target Scope:** Lines 1–273 (`FeatureRange`)
* **Reviewers:** Hostile Design Critic (`egm-design`), Structural Layout Validator (`egm-structural`), Technical Copy Editor (`egm-style`)
* **Standard:** `feature-spec-standard`

---

## 1. Executive Re-Audit Summary

Stage 7 re-evaluated `FeatureRange` following author updates to the Open Questions section. 

The re-audit identified critical blockers and internal specification contradictions that must be reconciled:
1. **Shared NAT Rate Limiting Contradiction (🔴 Blocker)**: The author answered Open Question #2 with *"keep the rate limiting logic as exist"* (retaining 20 req/min per IP across all `/api/v1/auth` routes). Under 5-minute access tokens (~0.2 refreshes/min/user), as few as 100 active users behind a campus or corporate NAT will completely saturate the 20 req/min quota on background refreshes alone, triggering cascading HTTP 429 rejections, token expiration, and widespread user logouts. Furthermore, this answer directly contradicts Section 6, Goal 6, Problem 4, and the architecture diagram, which mandate partitioning `/api/v1/auth/refresh` to 100 req/min.
2. **Claim Encoding Contradiction (🔴 Blocker)**: The author answered Open Question #1 with *"Incode the mask as a normal decimal integer not a string"*. However, Section 3, Section 4, and the implementation plan still specify string claim emission (`new Claim("perms", ToString(...))`) and string parsing (`uint.TryParse(permsClaim.Value)`). In standard ASP.NET Core `ClaimsIdentity`, claim values are strings; emitting a raw JSON number requires adding it via `SecurityTokenDescriptor.Claims` as an `int`/`long` object.
3. **Fictional Domain Permission Catalog (🔴 Blocker)**: The proposed `PermissionBits` enum (lines 110–133) invents 20 arbitrary CRUD permissions (`UsersRead`, `AssessmentsGrade`, `CoursesDelete`, `SystemMetricsRead`) that do not exist in the domain model, while omitting over 10 actual production permissions from `CsRaiders.Api.Domain.Constants.PermissionCatalog` (`course:read`, `course:purge`, `payment:verify`, `session:revoke`, etc.).
4. **Transaction Lifecycle Incompatibility (🔴 Blocker)**: Proposing manual transaction commits inside `RefreshTokenCommandHandler` conflicts with MediatR's ambient `TransactionBehavior<TMessage, TResponse>`, which owns transaction boundaries. Resolving permissions before validating token signatures or session state also introduces an unauthenticated query amplification DoS vector.
5. **Administrative Revocation Safety Gap (🟡 Major)**: Because access token permission claims lag administrative mutations by up to 5.5 minutes, commands that revoke critical permissions (`payment:*`, `role:manage`, `user:write`) must explicitly invoke `ISessionRepository.RevokeAllByUserIdAsync` to force immediate 5-second termination via `SessionValidationMiddleware.cs`.
6. **Structural Hierarchy & List Formatting Defects (🟡 Major / 🟢 Nit)**: Unindented answer lines in Open Questions malformed the CommonMark list AST, `Additional Resources` and `Future Improvements` remain orphaned as `###` subsections, and section dividers remain asymmetric.

---

## 2. Prioritized Review Findings

### 🔴 Blocker Findings

#### [BLOCKER 1] Shared NAT Denial of Service under 20 Req/Min IP Rate Limiter
* **Lens:** Design Critic & Copy Editor
* **Affected Scope:** Lines 20, 30, 42–43, 78, 205–213
* **Details:**  
  Retaining the existing 20 req/min per IP rate limiter on `/api/v1/auth` while slashing access token lifetime to 5 minutes guarantees service denial for multi-user NAT networks:
  $$\text{NAT Capacity Ceiling} = \frac{20\text{ requests/min}}{0.2\text{ refreshes/min/user}} = 100\text{ active users}$$
  In an office or campus network with $\ge 100$ active users, passive background refreshes alone consume 100% of the IP's quota. Any login, registration, or multi-tab refresh burst triggers HTTP 429, causing tokens to expire and forcibly logging users out.
* **Actionable Recommendation:** Reconcile the contradiction. Partition `/api/v1/auth/refresh` into an independent rate limiter policy (`auth-refresh`) with a limit of 100–120 req/min per IP (or keyed by `SessionId`), while keeping `/api/v1/auth/login` at 20 req/min per IP.

---

#### [BLOCKER 2] Claim Data Type Contradiction: JSON Integer vs. String Claim
* **Lens:** Design Critic & Copy Editor
* **Affected Scope:** Lines 40–41, Lines 150–155, Line 171, Line 330
* **Details:**  
  The author's note specifies encoding `perms` as a numeric integer, but Section 3 specifies `new Claim("perms", permissionsBitmask.ToString(CultureInfo.InvariantCulture))` and Section 4 uses `uint.TryParse(permsClaim.Value)`.
* **Actionable Recommendation:** Specify exact JSON integer serialization via `SecurityTokenDescriptor.Claims`:
  ```csharp
  descriptor.Claims = new Dictionary<string, object>
  {
      ["perms"] = (long)permissionsBitmask
  };
  ```
  In `PermissionAuthorizationHandler`, handle numeric claim extraction cleanly:
  ```csharp
  var permsClaim = context.User.FindFirst("perms");
  if (permsClaim != null && uint.TryParse(permsClaim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitmask))
  ```

---

#### [BLOCKER 3] Domain Model Hallucination: Fictional Permission Catalog
* **Lens:** Design Critic & Copy Editor
* **Affected Scope:** Lines 49, 54, Lines 108–134
* **Details:**  
  `PermissionBits` defines 20 synthetic flags (`UsersRead`, `AssessmentsGrade`, etc.). `PermissionCatalog.cs` defines lowercase colon-delimited constants:
  `course:read`, `course:write`, `course:purge`, `lesson:write`, `asset:write`, `question:write`, `quiz:write`, `user:read`, `user:write`, `role:manage`, `payment:read`, `payment:verify`, `payment:refund`, `notification:send`, `analytics:read`, `audit:read`, `settings:read`, `settings:write`, `session:multi`, `session:revoke`.
* **Actionable Recommendation:** Replace `PermissionBits` with a 1:1 bitwise mapping of the exact constants in `PermissionCatalog.cs` (`CourseRead = 1u << 0` for `"course:read"`, etc.).

---

#### [BLOCKER 4] Transaction Lifecycle Conflict & DoS Amplification
* **Lens:** Design Critic
* **Affected Scope:** Lines 29, 60, Lines 90–95, Lines 188–204
* **Details:**  
  1. `TransactionBehavior<TMessage, TResponse>` opens the database transaction before `RefreshTokenCommandHandler.Handle` and commits after. Manual commits inside the handler cause `InvalidOperationException`.
  2. Resolving permissions before validating the session row allows unauthenticated or replayed tokens to force expensive joins across `users` and `roles`.
* **Actionable Recommendation:**
  Validate the refresh token and session first (`session.TryRotate(...)`). If valid, resolve permissions from memory cache (`perms:{userId}`) or database, pass to token issuance, and let `TransactionBehavior` commit atomically.

---

### 🟡 Major Findings

#### [MAJOR 1] Administrative Revocation Safety Gap
* **Lens:** Design Critic
* **Affected Scope:** Line 34, Lines 242–246
* **Details:** Revoking critical permissions (`payment:*`, `role:manage`, `user:write`) takes up to 5.5 minutes to propagate across active access tokens.
* **Actionable Recommendation:** Mandate that commands altering sensitive roles/permissions in `RbacCommands.cs` call `ISessionRepository.RevokeAllByUserIdAsync` to force immediate 5-second termination via `SessionValidationMiddleware.cs`.

#### [MAJOR 2] Corrupted Claim Fallback Defect
* **Lens:** Design Critic & Copy Editor
* **Affected Scope:** Lines 168–185
* **Details:** If `"perms"` exists but fails parsing, the code falls through to legacy database queries. If present but corrupted, authorization must fail closed immediately.
* **Actionable Recommendation:** Execute legacy cache fallback *only* when `permsClaim == null`. If `permsClaim != null` and parsing fails, fail closed (`return Task.CompletedTask`).

#### [MAJOR 3] Structural Layout & List AST Malformation
* **Lens:** Structural Layout Validator
* **Affected Scope:** Lines 40–43, Lines 259–271
* **Details:**
  - Raw unindented answer text terminates tight list items in Open Questions.
  - `Additional Resources` and `Future Improvements` appear as orphaned `###` subsections.
  - Section dividers are missing between upper-half `##` sections.
* **Actionable Recommendation:** Indent resolutions under question bullets, promote trailing sections to `##`, and restore symmetric 80-character dividers between all `##` sections.

---

### 🟢 Nit Findings

* **[NIT 1] Uncalibrated Latency Claims**: Calibrate `< 1 ns` claims to sub-microsecond handler execution time.
* **[NIT 2] Redundant Meta-Tags**: Remove redundant `"Rejected by author:"` prefixes in Alternatives Considered.
* **[NIT 3] Clickable Relative Links**: Format paths in `Additional Resources` as clickable markdown links.
