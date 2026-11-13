# Feature Specification: Token Bitmask Authorization

**Feature Branch**: `002-token-bitmask-authorization`

**Created**: 2026-11-12

**Status**: Draft

**Input**: User description: "Implement the feature documented in `docs/designs/token_embedded_authorization_bitmask_claims.md`."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authorize Protected Requests from the Access Token (Priority: P1)

As an authenticated API user, I want protected actions to be authorized from my signed access token so that my request does not need a permission lookup during the request.

**Why this priority**: This is the core authorization behavior and removes the request-path dependency on permission storage availability and lookup latency.

**Independent Test**: Issue an access token containing a valid permission-mask claim, call a protected action requiring a represented permission, and confirm access is granted without a permission lookup.

**Acceptance Scenarios**:

1. **Given** an authenticated user has a valid access token containing a decimal-string permission mask that includes the required permission, **When** they call a protected action, **Then** the action is authorized.
2. **Given** an authenticated user has a valid access token containing a decimal-string permission mask that does not include the required permission, **When** they call a protected action, **Then** the action is denied.
3. **Given** an access token lacks the permission-mask claim, **When** its holder calls a protected action, **Then** the action is denied immediately and no permission lookup is attempted.
4. **Given** an access token has an empty, malformed, negative, or otherwise invalid permission-mask claim, **When** its holder calls a protected action, **Then** the action is denied immediately and no permission lookup is attempted.

---

### User Story 2 - Receive Compact, Correct Permission Claims (Priority: P1)

As an authenticated client, I want every newly issued access token to contain my effective permissions in a compact, precision-safe form so that both server and client permission checks remain correct.

**Why this priority**: New tokens must carry the required claim before protected requests can use the new authorization behavior.

**Independent Test**: Sign in users with different role and individual permission assignments, inspect their newly issued token claims, and verify that each claim represents exactly their effective permissions as a decimal string.

**Acceptance Scenarios**:

1. **Given** a user signs in successfully, **When** an access token is issued, **Then** it contains a `perms` claim encoded as a base-10 integer string representing the user's effective permissions.
2. **Given** the permission catalog's supported permissions are all granted to a user, **When** an access token is issued, **Then** every supported permission is represented without numeric precision loss.
3. **Given** a client decodes the token payload, **When** it evaluates a represented permission using arbitrary-precision integer support, **Then** it obtains the same allow or deny result as the API.
4. **Given** an unknown permission is requested by a protected action, **When** authorization is evaluated, **Then** access is denied rather than granting an unintended capability.

---

### User Story 3 - Refresh Safely After Permission Changes (Priority: P1)

As a user with an active session, I want my access token refreshed with my current effective permissions so that ordinary permission changes take effect promptly without ending my session unnecessarily.

**Why this priority**: The shorter access-token lifetime is the mechanism that safely propagates ordinary authorization changes.

**Independent Test**: Change a user's permissions, refresh a valid session after the previous access token expires, and verify the replacement token reflects the new effective permissions; test invalid refresh attempts separately and verify they do not trigger permission resolution.

**Acceptance Scenarios**:

1. **Given** a valid, active refresh session, **When** the client refreshes its access token, **Then** the replacement token contains the user's current effective permission mask and has a five-minute lifetime.
2. **Given** a refresh credential is invalid, expired, reused, does not match the session, or belongs to an inactive session, **When** it is presented, **Then** refresh is denied before permission data is resolved.
3. **Given** a user has no relevant permission changes between refreshes, **When** the user refreshes repeatedly, **Then** the service may reuse a valid cached effective-permission result while preserving the same authorization outcome.
4. **Given** an administrator changes ordinary permissions for an active user, **When** the user next obtains an access token, **Then** the change is reflected within five minutes plus up to 30 seconds of clock tolerance.

---

### User Story 4 - Immediately Contain Sensitive Permission Removal (Priority: P2)

As an administrator, I want removal of sensitive administrative capabilities to terminate affected active sessions quickly so that high-risk access is not retained for the ordinary propagation window.

**Why this priority**: It adds stronger protection for high-impact access changes while keeping ordinary changes non-disruptive.

**Independent Test**: Remove each designated sensitive capability from a user with active sessions and verify that those sessions can no longer make authenticated requests within the session-validation interval.

**Acceptance Scenarios**:

1. **Given** an active user loses a designated sensitive capability, **When** the change is completed, **Then** all of that user's active sessions are revoked.
2. **Given** a session has been revoked after sensitive capability removal, **When** its holder makes a subsequent authenticated request, **Then** access is denied within five seconds.
3. **Given** a user loses a non-sensitive capability, **When** the change is completed, **Then** the user's active session remains valid and the changed permission follows the ordinary propagation window.

### Edge Cases

- A token whose permission mask is zero permits no permission-protected actions.
- A claim that contains unsupported permission positions must not grant any recognized permission beyond those explicitly represented by the catalog.
- Missing, invalid, or changed permission assignments during sign-in or successful refresh result in a token that reflects only known effective permissions; a user who cannot authenticate is not issued a token.
- Concurrent refresh attempts for one session permit only the valid rotation outcome; rejected attempts do not cause permission-resolution work.
- Shared network addresses must not cause legitimate refresh traffic to be blocked by the stricter limit applied to sign-in and other authentication routes.
- Existing sessions remain governed by their established session lifetime; the five-minute access-token lifetime alone does not terminate a session.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST include a `perms` claim in every newly issued access token after successful sign-in or successful token refresh.
- **FR-002**: The `perms` claim MUST be a base-10 integer string that represents the user's effective permissions and preserves all supported permission positions without client numeric precision loss.
- **FR-003**: The system MUST support a fixed catalog of up to 64 permission positions and MUST assign each recognized permission a stable, unique position.
- **FR-004**: The system MUST authorize a permission-protected action solely from the signed access token's valid `perms` claim and the permission required by that action.
- **FR-005**: The system MUST deny a permission-protected action when the `perms` claim is missing, malformed, out of range, or does not include the required recognized permission.
- **FR-006**: Authorization under FR-004 and FR-005 MUST NOT fall back to a permission cache or persistent permission lookup.
- **FR-007**: Newly issued access tokens MUST expire five minutes after issuance.
- **FR-008**: The system MUST retain the existing string-based permission records and granular permission audit information; this feature MUST NOT require a data-schema migration.
- **FR-009**: Before resolving permissions during refresh, the system MUST validate the refresh credential, validate the associated session, and confirm a successful session rotation; any failure MUST end the refresh without permission-resolution work.
- **FR-010**: After a successful refresh rotation, the system MUST issue a replacement access token using the user's current effective permissions and MAY reuse a valid, invalidation-aware cached permission-mask result.
- **FR-011**: Removing any designated sensitive capability (`role:manage`, `payment:refund`, `user:write`, or `session:revoke`) from a user MUST revoke all active sessions for that user; changes to other permissions MUST follow the ordinary access-token propagation window.
- **FR-012**: The system MUST preserve session validation for revocation, inactivity, suspension, and concurrent-session controls on every authenticated request.
- **FR-013**: The refresh route MUST allow up to 100 requests per minute for each network-address partition, while other authentication routes remain limited to 20 requests per minute for each network-address partition.
- **FR-014**: The feature MUST reject legacy access tokens that lack `perms`; compatibility fallback for such tokens is out of scope.

### Key Entities *(include if feature involves data)*

- **Permission catalog**: The bounded set of recognized permission names, each with one stable mask position.
- **Permission mask**: The compact unsigned value representing a user's effective recognized permissions, transmitted as a decimal string claim.
- **Access token**: A signed, five-minute credential containing identity, session, and permission-mask information.
- **Refresh session**: The long-lived authenticated session that governs access-token replacement, rotation validity, and revocation state.
- **Effective permissions**: The result of combining a user's role-assigned permissions with their individual permission overrides.
- **Sensitive capability**: A permission whose removal requires immediate active-session revocation rather than ordinary propagation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of newly issued access tokens include a valid decimal-string `perms` claim representing the recipient's effective recognized permissions.
- **SC-002**: 100% of permission-protected requests with a valid token are authorized or denied without a permission cache or persistent permission lookup on the request path.
- **SC-003**: 100% of missing or malformed `perms` claims on permission-protected requests are denied without a permission lookup.
- **SC-004**: Ordinary permission additions and removals are reflected in newly issued access tokens within five minutes plus 30 seconds of clock tolerance.
- **SC-005**: 100% of designated sensitive-capability removals revoke the affected user's active sessions, and those sessions are rejected within five seconds.
- **SC-006**: At least 99% of successful refreshes with unchanged authorization state avoid resolving permissions from persistent records, measured over a representative sustained-load test.
- **SC-007**: A client using arbitrary-precision integer support produces the same allow/deny result as the API for every one of the supported permission positions.
- **SC-008**: Shared-network refresh traffic of up to 100 requests per minute is not rejected solely by the authentication-route rate limit.

## Assumptions

- The application is pre-production, so invalidating access tokens that lack `perms` does not require a transition period.
- The existing signed-token validation mechanism protects claim integrity; clients cannot alter a permission mask without invalidating the token.
- The current permission catalog contains 20 permissions and remains within the 64-position feature boundary.
- Clients that inspect permissions use arbitrary-precision integer handling for the decimal-string claim; server authorization remains the source of truth.
- Standard permission changes are acceptable within the stated five-minute-plus-tolerance propagation window.
- Existing refresh-session lifetime, inactivity, suspension, and concurrent-session policies remain unchanged.

## Out of Scope

- Dynamic creation of permissions or support for more than 64 permission positions.
- Database schema changes to store permission masks.
- A compatibility fallback for access tokens issued without the `perms` claim.
- Eliminating session validation or making ordinary permission removal instantaneous for active access tokens.
