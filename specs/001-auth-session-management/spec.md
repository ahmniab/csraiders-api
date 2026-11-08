# Feature Specification: Authentication & Session Management with Fine-Grained Access Control

**Feature Branch**: `001-auth-session-management`

**Created**: 2026-11-08

**Status**: Draft

**Input**: User description: "there is an implementation plan for auth service here @docs/designs/authentication_and_session_management.md @docs/designs/CS Raiders — Authentication & Session Management (Revision 2).md. the revision is just modifications for the main plan. analyse them and specify"

**Source documents analyzed**: The base design (dated 2026-11-07) and Revision 2 (dated 2026-11-08, which supersedes the base). Revision 2 closes both open questions from the base design, resolves all review findings, and is therefore the authoritative source for every behavior below. Where the base and Revision 2 disagree (session cache expiry, login concurrency control, refresh grace window, lockout keying and status codes, CSRF approach, registration resend, email delivery, suspension, change password, cleanup, pepper versioning, RBAC cache invalidation), **Revision 2 wins**.

---

## Clarifications

### Session 2026-11-08

- Q: How should privileged accounts (instructors and administrators) come to exist, given self-registration always creates a Student account? → A: Self-registration only ever creates Student accounts; existing administrators elevate trusted registered users to privileged roles via the admin management endpoints (FR-029).
- Q: Should registration accept any email address, or be restricted to specific (institutional) email domains? → A: Registration accepts any valid, deliverable email address (no domain allow-list or invitation codes in this milestone).
- Q: How long should security audit events (lockouts, replay detections, suspensions, policy changes, verification failures) be retained before cleanup? → A: 30 days, enforced by the scheduled cleanup job.
- Q: Which operational observability signals must the authentication subsystem emit as requirements, beyond log redaction and audit rows? → A: Security counters plus structured per-request logging with trace correlation, all respecting secret redaction.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Student Account Registration & Email Verification (Priority: P1)

A prospective student creates an account by submitting their name, email, and password. The system emails them a single-use verification code; entering that code activates the account. If the email never arrives, the student can request a fresh code.

**Why this priority**: Nothing else in the platform works without verified accounts. This is the entry point for all users and the backbone of anti-abuse (proving email ownership).

**Independent Test**: Can be fully tested by registering a new account, completing (or failing and resending) verification, and confirming only verified accounts can sign in.

**Acceptance Scenarios**:

1. **Given** an email address not yet registered, **When** the user submits valid registration details, **Then** the system accepts the request, stores a hashed password plus a single-use verification code derived from a cryptographically secure random value, emails the code, and returns the same generic success response regardless of whether the email already exists.
2. **Given** a pending registration with a correct code within its validity window, **When** the user submits the code, **Then** the account is activated with the default learner role, the pending record (including any stored password) is purged, and the user can sign in.
3. **Given** a pending registration has consumed its allowed verification attempts with wrong codes, **When** the user then submits the correct code, **Then** the code is rejected and no account is created; the user must restart registration (or resend, if still within limits).
4. **Given** the original verification email was never delivered, **When** the user requests a fresh code after the cooldown, **Then** the new code replaces the old one entirely — the old code no longer works and the attempt counter is reset.
5. **Given** an email that is already a fully registered user, **When** someone submits registration details for it, **Then** the system responds identically to a new registration (same status, same timing) and never reveals that the email is taken.
6. **Given** a pending registration that is still valid, **When** the same email submits new registration details with a different password, **Then** the originally stored password is preserved (re-registering never overwrites a pending password) and the request behaves as a resend.

---

### User Story 2 - Login with Credential Verification & Durable Brute-Force Defense (Priority: P1)

Users sign in with email and password. Repeated failures trigger progressive delays and a temporary per-attempt-source block that survives restarts, without leaking whether an email exists and without letting an attacker lock out a victim from other sources.

**Why this priority**: Login is the most attacked endpoint. Credential stuffing and password guessing must be stopped durably before any session behavior matters.

**Independent Test**: Can be fully tested by submitting correct and incorrect credentials at varying rates and confirming delays, blocks, and uniform responses — including after a service restart.

**Acceptance Scenarios**:

1. **Given** valid credentials for an account with no active session, **When** the user signs in, **Then** a short-lived access credential and a long-lived refresh credential (delivered in a secure browser cookie) are issued, and the account's failure counters are cleared.
2. **Given** 3 consecutive failed attempts for the same email+source pair, **When** further attempts are made, **Then** each is answered with a progressively increasing wait requirement (capped) applied **before** password verification, indicated to the caller with a uniform failure response plus a retry hint.
3. **Given** 10 total failures for the same email+source pair, **When** any attempt from that pair is made during the block window, **Then** it is rejected with a uniform failure response and a retry hint — including attempts with the correct password. The block persists across service restarts.
4. **Given** an account is blocked from one source (IP), **When** the legitimate owner attempts to sign in from a different source, **Then** they can sign in normally (the block is scoped to the email+source pair, not the account globally).
5. **Given** an email that does not exist, **When** credentials are submitted, **Then** the system performs equivalent cryptographic work and returns an identical failure response to a wrong-password case, and the failure is recorded under the same email+source keying as known emails.
6. **Given** many attempts for one email arriving from many different sources (distributed attack), **When** a soft cross-source failure threshold is exceeded within a rolling window, **Then** further attempts for that email receive an added delay — but the correct owner is still never hard-blocked from authenticating.
7. **Given** a suspended account, **When** credentials are submitted, **Then** the response is exactly the same uniform failure as a wrong password (suspension is never revealed at login).

---

### User Story 3 - Single Active Session per Student (Priority: P1)

By default, each user has exactly one active session. A sign-in from a second device immediately supersedes the first, with no exploitable grace period for the old device.

**Why this priority**: Preventing account sharing is a core product integrity requirement (assessment fairness); it demands immediate, race-free supersession.

**Independent Test**: Can be fully tested by signing in on two devices and confirming the first device's subsequent requests fail promptly, and by racing simultaneous logins and confirming exactly one active session survives.

**Acceptance Scenarios**:

1. **Given** a student is active on Device A, **When** the student signs in on Device B, **Then** Device A's session is marked superseded at the same instant Device B's is created, and Device A's next request fails with a session-superseded rejection.
2. **Given** Device A's session was just superseded, **When** Device A makes its next request, **Then** the rejection occurs within 5 seconds at most (and immediately in the common case), not after the full access-credential lifetime.
3. **Given** many simultaneous sign-in attempts for the same student, **When** they all complete, **Then** exactly one active session exists and no errors or inconsistent session states are produced.
4. **Given** a user holds the explicit multi-session permission (e.g., administrators, instructors), **When** they sign in on a device that would exceed the configured concurrency cap, **Then** the oldest active session is ended with a cap-exceeded reason before the new one is created.

---

### User Story 4 - Refresh Token Rotation & Theft Detection (Priority: P1)

Long-lived refresh credentials rotate on every use. Reusing a rotated credential after a short grace period is treated as theft: the entire session is killed instantly.

**Why this priority**: Stolen refresh credentials are the primary session-hijack vector for long-lived browser sessions; replay detection is the last line of defense.

**Independent Test**: Can be fully tested by rotating a refresh credential, replaying the old one (both inside and after the grace window), and confirming benign retry versus theft-detection behavior.

**Acceptance Scenarios**:

1. **Given** a valid, active session, **When** the client presents the current refresh credential, **Then** a new refresh credential and access credential are issued, the old one can no longer be used as current, and the session's inactivity window is extended.
2. **Given** a refresh credential was rotated within the last few seconds (grace window), **When** the just-retired credential is presented again (retry or concurrent tab), **Then** the request succeeds with another normal rotation and the session is not penalized.
3. **Given** a retired refresh credential is presented after the grace window has passed, **When** it is exchanged, **Then** the whole session is revoked immediately with a reuse-detected reason, a security event is recorded, cookies are cleared, and the exchange is rejected — even if the database work from the legitimate rotation is being rolled back elsewhere.
4. **Given** a refresh request, **When** it arrives without the expected cookie, origin, or required custom header, **Then** it is rejected before any credential evaluation (cross-site request forgery protection).
5. **Given** an expired, revoked, or idle-expired session's refresh credential, **When** it is presented, **Then** the exchange is rejected with a session-invalid response and cookies are cleared.

---

### User Story 5 - Self-Service Session Visibility & Termination (Priority: P2)

Users can see their active sessions (device, time, source) and revoke an individual session or all other sessions.

**Why this priority**: Gives users direct control over account hygiene and a self-serve remedy for "I forgot to log out on another device".

**Independent Test**: Can be fully tested by creating multiple sessions, listing them, revoking one or all others, and confirming revoked devices stop working.

**Acceptance Scenarios**:

1. **Given** a user with multiple active sessions, **When** they list their sessions, **Then** only their own active sessions are shown, including creation time, last activity, device identifier, and source — never credential material.
2. **Given** a specific session is revoked by its owner, **When** the affected device next makes a request, **Then** it is rejected as superseded.
3. **Given** "revoke all other sessions" is chosen, **When** it executes, **Then** every active session except the current one is terminated.

---

### User Story 6 - Password Change & Self-Service Reset (Priority: P2)

Users change their own password with the current one; users who forgot their password recover access through a single-use emailed reset link that revokes all sessions.

**Why this priority**: Standard account recovery; credential compromise remediation requires immediate global session invalidation.

**Independent Test**: Can be fully tested by changing a password (old required, other sessions die) and by completing a forget/reset cycle with uniform anti-enumeration responses.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they change their password with the correct current password, **Then** the credential is updated and all **other** sessions are revoked (the current device stays signed in).
2. **Given** any email address, **When** a reset is requested, **Then** the response is uniformly positive regardless of whether the account exists.
3. **Given** a valid reset within its validity window, **When** it is used once, **Then** the password is updated, **all** sessions are revoked with a password-reset reason, and the token can never be reused.
4. **Given** a new reset is requested, **When** it is issued, **Then** any previously issued, not-yet-used reset for that account becomes unusable.

---

### User Story 7 - Fine-Grained Permissions with Live Policy Reconciliation (Priority: P2)

Access control is permission-based rather than role-name-based. Effective permissions combine role bundles with per-user grants and denials (explicit denial always wins), and any policy change takes effect for affected users within seconds — including downgrading multi-session rights.

**Why this priority**: All course, grading, and administrative access depends on it; live reconciliation closes the gap between a policy change and enforcement.

**Independent Test**: Can be fully tested by granting/revoking a permission and verifying endpoint access flips within the cache window without re-login, plus verifying multi-session downgrade reconciliation.

**Acceptance Scenarios**:

1. **Given** a user's roles, grants, and denials, **When** endpoint access is evaluated, **Then** it follows: (role permissions ∪ user grants) minus user denials, with user denials overriding everything.
2. **Given** an administrator modifies any role or user override, **When** affected users make their next request, **Then** enforcement reflects the change within the short permission cache window (30 seconds) without them re-authenticating.
3. **Given** a user with multi-session rights and the maximum allowed sessions loses those rights, **When** the change is applied, **Then** in one safe step the older sessions are revoked with a policy-reduced reason and the newest session is constrained to single-session mode — never producing a duplicate-active-session conflict.
4. **Given** an administrator, **When** they attempt to grant a permission they themselves do not hold, **Then** the action is blocked (privilege escalation guard).

---

### User Story 8 - Administrative Suspension & Data Hygiene (Priority: P3)

Administrators can suspend users (instantly locking them out) and the system automatically purges expired/obsolete authentication records on a schedule.

**Why this priority**: Operational housekeeping and incident response; important, but not on the critical path of core flows.

**Independent Test**: Can be fully tested by suspending a user (sessions die instantly, login blocked uniformly) and confirming the scheduled job removes only the intended aged records.

**Acceptance Scenarios**:

1. **Given** a user is suspended, **When** their active sessions are checked, **Then** all are revoked immediately with a suspension reason and their caches are evicted.
2. **Given** a suspended user, **When** they attempt to sign in, **Then** the response is the same uniform failure as an unknown/wrong password.
3. **Given** the periodic cleanup job runs, **When** it executes, **Then** it removes aged-out sessions, expired pending registrations, used/expired reset tokens, and stale failure-tracking rows — and nothing that is still needed.

---

## Edge Cases

- **Password hashing under load**: When many sign-ups or sign-ins arrive concurrently, hashing work is bounded so the service stays responsive; excess requests are shed with a service-unavailable response rather than queueing indefinitely.
- **Email delivery pipeline**: Delivery is asynchronous through a bounded in-process queue (drop-and-log when full); a lost email is recoverable by user-initiated resend, not by data guarantees.
- **Refresh credential presented twice at the same instant** (two tabs): within the grace window both succeed as rotations; only one race may win, and the loser retries naturally.
- **Secrets in observability**: passwords, codes, refresh/reset tokens, and credential hashes never appear in logs or audit records, by an explicit column/field-name deny-list.
- **Security events survive failures**: lockouts, code failures, and replay detections are recorded through an isolated write path so they persist even when the surrounding request transaction rolls back.
- **Clock/timing**: access credentials include an issuance time and small validation skew is tolerated; revocation remains the authority regardless of remaining access-credential lifetime.
- **Unknown emails never timing-diverge**: identical cryptographic workload for known and unknown emails, identical responses.
- **Session-with-no-activity**: an idle session has an inactivity lifetime (shorter than the absolute lifetime) which, when exceeded, ends the session even without revocation.

## Requirements *(mandatory)*

### Functional Requirements

**Registration & verification (FR-60 series from the design)**

- **FR-001**: System MUST allow prospective users to register with name, email, and password from any valid, deliverable email address (no domain restriction in this milestone), and MUST verify email ownership with a 6-digit random code before activating the account.
- **FR-002**: System MUST store the registration code only as a keyed hash bound to the email address, allow a maximum of 5 verification attempts, and expire attempts after 10 minutes.
- **FR-003**: Registration attempts for already-registered emails MUST produce the same response and timing as new registrations (no account existence disclosure).
- **FR-004**: Registration codes MUST be re-issuable via resend, with a 60-second cooldown per email and a maximum of 5 sends per hour per email; a resent code MUST fully replace the previous one (old code invalid, attempts reset, expiry extended).
- **FR-005**: Re-registering an email that has a valid pending registration MUST NOT overwrite the stored password.

**Credentials & login (FR-61 series)**

- **FR-006**: System MUST verify passwords using a memory-hard, salted, peppered hashing scheme; inputs MUST be Unicode-normalized before verification, and weaker/older parameter versions MUST be transparently upgraded on successful login.
- **FR-007**: Passwords MUST be 8–128 characters after normalization.
- **FR-008**: Failed logins MUST be tracked durably per (email, source-IP) pair with progressive delays for failures 4–9 (exponential, capped at 15 seconds) and a 15-minute block at failure 10, enforced **before** password verification, communicated with a uniform failure response and a retry hint.
- **FR-009**: A global soft per-email failure threshold (30 failures in 15 minutes across all sources) MUST add delay only, never a hard block, so a distributed attack cannot lock out the legitimate owner.
- **FR-010**: Successful login MUST clear the (email, source-IP) failure counters.
- **FR-011**: Suspended or unknown accounts, and wrong passwords, MUST all yield identical login failure responses (no status distinction, no 423-style codes).
- **FR-012**: Sign-in for an account at or above its concurrency allowance MUST end the superseded/cap-exceeded session(s) and create the new one in a single safe step serialized per user, such that concurrent sign-ins can never produce a duplicate-active-session state.
- **FR-013**: Users holding the `session:multi` permission MAY have up to a configurable cap of concurrent sessions (default 3); exceeding the cap MUST end the oldest session with a `cap_exceeded` reason.
- **FR-014**: All users without `session:multi` MUST be constrained to exactly one active session; a new sign-in MUST immediately supersede prior active sessions with a `superseded` reason.

**Sessions & tokens (FR-63/64 series)**

- **FR-015**: Active sessions MUST be validated on **every** authenticated request; revoked, expired, or idle-expired sessions MUST be rejected with a session-superseded failure and cleared cookies.
- **FR-016**: Revocation (supersession, logout, password change/reset, suspension, policy reduction, theft detection) MUST become effective for the affected device **immediately in the common case** and no later than 5 seconds in the worst case.
- **FR-017**: Access credentials MUST be short-lived (15 minutes) and MUST NOT carry roles, permissions, or capability claims; identity + session linkage only.
- **FR-018**: Refresh credentials MUST be long-lived (30 days), single active per session, delivered only via secure, HttpOnly, Strict same-site cookies with the host-prefixed name, and rotated on every successful use with the retired value retained exactly one generation.
- **FR-019**: Presenting a retired refresh credential **within** a 15-second grace window MUST be treated as a benign retry (rotate again); presenting it after the window MUST revoke the entire session with a `reuse_detected` reason, emit a security event, clear cookies, and reject the exchange — and this revocation MUST persist even if the request's other work fails.
- **FR-020**: Session inactivity (default 14 days) MUST end a session independently of its absolute lifetime (30 days); each refresh extends the inactivity window.
- **FR-021**: Per-request database load for session validation MUST be minimized by short-lived local status caching (≤5 seconds, absolute) while preserving FR-016's revocation bounds; explicitly an in-process mechanism with no shared external cache in this milestone (single-instance deployment).
- **FR-022**: Users MUST be able to log out, which revokes their server-side session immediately and clears cookies.

**Password recovery & management (FR-65 series)**

- **FR-023**: Users MUST be able to request a password reset for any email with a uniform positive response (anti-enumeration); reset tokens MUST be 30-minute, single-use, and a new request MUST invalidate any prior unused token.
- **FR-024**: A successful password change or reset MUST revoke the user's other (or all, respectively) active sessions with explicit reasons (`password_changed` / `password_reset`).

**Access control (FR-70 series)**

- **FR-025**: All protected endpoints MUST be guarded by explicit, permission-based authorization (never implicit role-name checks); a request without the required effective permission MUST be rejected as forbidden.
- **FR-026**: Effective permission resolution MUST follow: (role permissions ∪ per-user grants) minus per-user denials, with denials taking precedence.
- **FR-027**: Administrative role/permission changes MUST enforce a privilege-escalation guard: an administrator cannot grant permissions they do not themselves hold.
- **FR-028**: Concurrency-rights downgrade (loss of `session:multi`) MUST trigger immediate reconciliation of that user's sessions within one transaction — older sessions revoked `policy_reduced`, newest constrained to single-session mode.
- **FR-029**: Administrative endpoints MUST require the corresponding management permissions (`role:manage`, `user:manage`) with no anonymous access.
- **FR-037**: Self-registration MUST only ever assign the default Student role; elevated roles (instructor, administrator) MUST be granted exclusively by an existing administrator through the admin endpoints (account elevation flow).

**Cross-cutting**

- **FR-030**: Authentication-adjacent IP-rate limiting (20 requests/minute per IP on auth endpoints) MUST run before any cryptographic work; per-email limiting MUST be handled later in the flow.
- **FR-031**: Every cookie-acting endpoint (refresh, logout) MUST require same-origin verification and an application-required custom header that cross-site pages cannot silently attach; content-type validation applies.
- **FR-032**: Security-relevant events (lockouts, verification failures, replay detections, suspensions, policy changes) MUST be audited, with all secret material redacted by an explicit field-name deny-list, and audit writes MUST survive transaction rollbacks.
- **FR-033**: Login/registration cryptographic work MUST be bounded so the service cannot be saturated into thread starvation; on sustained overflow it MUST shed load with an explicit unavailability response.
- **FR-034**: A scheduled cleanup MUST retire: sessions revoked/expired more than 30 days ago, pending registrations expired, reset tokens used/expired older than 7 days, failure-tracking rows inactive more than 24 hours, and security audit events older than 30 days.
- **FR-035**: Email delivery for codes and reset links MUST flow through a bounded in-process queue with background delivery, retries with backoff, and logging that redacts the email address, codes, and links; email loss is recoverable via resend (no outbox in this milestone).
- **FR-036**: Every user-facing failure above MUST return a stable, documented error code so clients (SPA) can react consistently.
- **FR-038**: The subsystem MUST emit structured log records per request carrying a trace correlation identifier, and MUST expose operational counters for the key security signal set: failed-login lockouts and blocks, verification failures, refresh-credential replay detections, session revocations by reason, email-queue-full events, and repeated delivery failures. All logs, counters, and metrics MUST respect the secret-redaction deny-list.

### Key Entities *(include if feature involves data)*

- **User**: account with name, normalized unique email, password credential (with hash parameters + pepper version), suspension state, role memberships, per-user permission grants/denials.
- **PendingRegistration**: pre-activation record holding the would-be password credential (hashed), hashed verification code, remaining attempts, expiry.
- **Session**: the authoritative record of one device's authenticated state — user linkage, creation time, last-seen time (debounced), absolute expiration, idle expiration, single-vs-multi enforcement flag, refresh credential hash + one previous hash + rotation timestamp, revocation time + reason.
- **LoginAttempt**: durable failure-tracking row keyed by (normalized email, source IP) with failure count, delay-until, block-until.
- **PasswordResetToken**: single-use, hashed, 30-minute credential for recovery.
- **Role / Permission**: named role bundling permissions; user-level grant/deny overrides; effective resolution is the union-minus-denials rule.
- **SecurityEvent**: audit record of security-relevant actions and failures with redacted payloads.
- **EmailMessage**: queued delivery unit (verification codes, reset links, notices).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new user completes registration through email verification in under 5 minutes end-to-end (excluding email transit), and 95%+ of issued verification codes succeed on first attempt when entered correctly.
- **SC-002**: Login responses return in under 1 second at the 95th percentile under normal load, while remaining byte-for-byte and timing-equivalent between "unknown email" and "wrong password" cases.
- **SC-003**: A session revoked for any reason stops working on the affected device within 5 seconds at most (measured from revoke commit to next-request rejection), with near-immediate enforcement in the common case.
- **SC-004**: Racing 10 concurrent sign-ins for one account always leaves **exactly one** active session with zero errors — verified repeatedly.
- **SC-005**: An attacker replaying a retired refresh credential after the grace window triggers session-wide revocation 100% of the time, while legitimate rapid retries within the window succeed 100% of the time.
- **SC-006**: Under a simulated 10-failure attack on one (email, source) pair, the block engages before password verification and persists across a service restart; the same email from a different source still authenticates.
- **SC-007**: Per-request database reads for session validation are reduced by at least 95% relative to validating every request directly, while holding the SC-003 revocation bound — measured under 500–1,500 requests/second load.
- **SC-008**: Zero secret material (passwords, codes, refresh/reset tokens, credential hashes) appears in any log or audit record, verified by inspection across all flows.
- **SC-009**: After any role or override change, affected users' enforcement reflects the change within 30 seconds without re-authentication, verified across at least two distinct permissions.
- **SC-010**: A user whose verification or reset email was lost completes recovery via resend in under 2 minutes without support intervention.
- **SC-011**: Conducting known credential-stuffing patterns against the login endpoint produces zero successful unauthorized authentications across the test suite.

## Assumptions

- **Single-instance deployment** for this milestone: the in-process session/permission caches and in-process email queue assume one application process behind one reverse proxy. Adding a second instance would require a shared invalidation mechanism first (documented upgrade path).
- **Deployment topology**: a single reverse proxy terminates TLS on the same host and forwards requests; the application is not directly reachable, and the proxy overwrites (not appends) forwarding headers so client IP resolution is trusted and non-spoofable. A test asserts that spoofed forwarding headers from untrusted sources are ignored.
- **Local development** emulates production transport security (trusted local certificates) so host-prefixed secure cookies function identically; integration tests use HTTPS base addresses.
- **Single-tenant SPA + API on one host origin** (no CORS needed); all third-party/social/passwordless sign-in is out of scope for this milestone and access tokens intentionally carry no permission claims.
- **Registration scope**: public and open to any email domain in this milestone; no invitations, institutional allow-lists, or bot defenses beyond the specified rate limits and cryptographic work.
- **Defaults carried from the design**: multi-session cap = 3; idle session lifetime = 14 days; absolute session lifetime = 30 days; refresh grace window = 15 seconds; OTP = 5 attempts / 10 minutes / 60-second resend cooldown / 5 per hour; lockout delays 2^(n−3) seconds capped at 15 s with a 15-minute block at the 10th failure; email queue capacity 1000 with 3 delivery attempts; cleanup retention per FR-034.
- **Session cache strategy** follows Revision 2 (absolute, not sliding, ≤5s expiry with explicit eviction on every server-side revoke) — resolving the base design's "sliding 5–10s" ambiguity, which would otherwise allow a continuously-active session to skip revalidation indefinitely.
- **HTTP status usage** follows Revision 2 (uniform 401 for all login failures; 202-style generic acceptance for registration/resend/reset; no 423).
- **Only one generation** of retired refresh credential is tracked; theft of a credential two or more rotations old is a documented accepted limitation with a token-family table as the upgrade path.
- **Privilege provisioning**: no invite flows, no bootstrap-created privileged accounts, and no password recovery needed for never-registered elevated users; the first administrator is provisioned out-of-band (install-time) and every subsequent privileged account is an elevation of a registered user.
- **Email delivery loss on crash** is accepted because users can resend; no durable outbox in this milestone.
