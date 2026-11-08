# Stage 6 Comprehension Test Report (Re-Audit)

**Target Document:** [`concepts_brief.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md) (Lines 1–55)  
**Reviewer:** Comprehension Goldfish (`egm-comprehension`)

---

## What this proposes
The document proposes an authentication, session lifecycle, and fine-grained role-based access control (RBAC) subsystem for the CS Raiders ASP.NET Core backend relying solely on PostgreSQL 16 as the source of truth without external caches like Redis. It pairs short-lived (15-minute) stateless JWT access tokens with stateful 30-day rotating refresh tokens stored in secure HTTP-only cookies (`__Host-csraiders_rt`), validated against the database on every authenticated request via `SessionValidationMiddleware`. User concurrency is restricted to single sessions by default using advisory lock serialization and database constraints, bypassable via a `session:multi` permission. The architecture further incorporates Argon2id password hashing, HMAC-SHA256 registration OTPs, two-tier brute-force defenses with durable PostgreSQL lockout tracking, and in-memory caching (30s) strictly for evaluated effective permissions (`(Roles ∪ Grants) \ Denies`).

## How the existing system works, per this document
The document describes no existing authentication or session management system. It presents the subsystem as a greenfield architecture for Milestone 0 of the CS Raiders backend. The text mentions no legacy auth infrastructure, existing user schemas, or migration pathways from a prior implementation.

---

## Findings

### 1. Inconsistent and Undefined Session Concurrency Constraint Nomenclature
* **Severity:** 🟡 Major
* **Affected Scope:** Line 8 (`enforce_single_session`) and Line 16 (`uq_sessions_one_active`)
* **Discrepancy Description & Rationale:**
  Line 8 references "database-enforced single-session concurrency constraints (`enforce_single_session`)", while Line 16 references preventing race conditions on "`uq_sessions_one_active`". It is unclear whether `enforce_single_session` is a table column, a database trigger function, a configuration parameter, or a preliminary name for the unique index `uq_sessions_one_active`. For a new engineer reading the brief, the relationship between these two named database artifacts is ambiguous and needs clarification inline.

### 2. Vague Explanation for `last_seen_at` Write Throttling
* **Severity:** 🟡 Major
* **Affected Scope:** Line 14
* **Discrepancy Description & Rationale:**
  Line 14 identifies database contention in `SessionValidationMiddleware` as a primary risk, stating that PostgreSQL will become the primary throughput bottleneck unless "throttled writes for `last_seen_at` are rigorously enforced." However, the brief does not explain how throttling is achieved. Because the brief explicitly rejects Redis and in-memory session caches, it is unclear whether throttling relies on an in-memory middleware timestamp filter (e.g., updating only if `last_seen_at < utcNow - 5 minutes`), a database trigger debounce, or an asynchronous channel. Leaving this mechanism unspecified leaves a critical performance risk unresolved.

### 3. Undefined "Dummy Verification" Anti-Enumeration Mechanism
* **Severity:** 🟢 Nit
* **Affected Scope:** Line 18 and Line 52
* **Discrepancy Description & Rationale:**
  Lines 18 and 52 mention "dummy verification for unknown emails" and "anti-enumeration dummy hashing" without defining the design pattern. While standard in timing-attack mitigation (performing a dummy Argon2id calculation when the user lookup returns null), the brief should briefly state where the dummy hash/salt originates and confirm that it is subject to the same Layer 1 rate limiting to prevent attacker-induced CPU starvation.

### 4. Undefined Out-of-Band Audit Logging Sink / Mechanism
* **Severity:** 🟢 Nit
* **Affected Scope:** Line 33
* **Discrepancy Description & Rationale:**
  Line 33 includes "Out-of-band security audit logging for failed operations" as an in-scope goal, but does not define what infrastructure is used for "out-of-band" execution within ASP.NET Core (e.g., `System.Threading.Channels`, an `IHostedService` background queue, or an independent short-lived DbContext connection). Providing a one-phrase definition will make the scope self-contained.
