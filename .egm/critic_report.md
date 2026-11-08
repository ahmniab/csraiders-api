# Stage 7 Critic Review Report

**Target Document:** [`concepts_brief.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md) (Lines 1–55)  
**Standard Evaluated:** [`feature-spec-standard.md`](file:///home/ahmniab/.gemini/config/plugins/egm/resources/document_standards/feature-spec-standard.md)  
**Reviewers:** Hostile Design Critic (`egm-design`), Structural Layout Validator (`egm-structural`), Technical Copy Editor (`egm-style`)

---

## Reviewer Status

| Reviewer | Status |
| :--- | :--- |
| `egm-design` | completed |
| `egm-structural` | completed |
| `egm-style` | completed |

---

## Prioritized Findings (Blockers -> Major -> Nits)

### 1. Database Saturation & Single Point of Failure in `SessionValidationMiddleware`
* **Severity:** 🔴 Blocker
* **Reviewer Lens:** `egm-design`
* **Affected Scope:** Lines 13–14, 30, 42–44
* **Discrepancy Description:**
  Executing an unbuffered, synchronous PostgreSQL query on every authenticated request to inspect `sessions` by `sid` will rapidly saturate the default Npgsql connection pool under standard production traffic (e.g., 500–1,500 RPS). This causes thread pool starvation, connection acquisition timeouts, and cascading HTTP 500/503 errors across the entire API. The assertion that "PostgreSQL handles the expected query volume when backed by partial indexes" provides no quantified throughput targets, connection pool limits, or pooler topologies.
* **Rationale & Recommendation:**
  Define concrete mitigations:
  1. Introduce a bounded short-TTL cache (5–10s `IMemoryCache` with sliding expiration) or PostgreSQL `LISTEN/NOTIFY` pub/sub invalidation on session revocation.
  2. Document PgBouncer connection pooling in transaction mode, read-replica offloading for active session lookups, and clear throughput ceilings.

---

### 2. Missing Mandatory Sections: Problem Statement & Proposed Architecture
* **Severity:** 🔴 Blocker
* **Reviewer Lens:** `egm-design`, `egm-structural`
* **Affected Scope:** Document Structure (Lines 6–10)
* **Discrepancy Description:**
  The brief violates core mandates of `feature-spec-standard.md`. It jumps directly from a high-level `Overview` into `Analysis / Vulnerability Points`, omitting:
  1. `## Problem Statement`: Business motivation, user problem, and security justification for a bespoke authentication subsystem over standard solutions (ASP.NET Core Identity, OpenIddict, Keycloak).
  2. `## Proposed Architecture`: Structural component layout, middleware pipeline order diagram, and data flow specification.
* **Rationale & Recommendation:**
  Add a dedicated `## Problem Statement` section and a `## Proposed Architecture` section containing an ASCII/Mermaid component interaction diagram and middleware pipeline sequence.

---

### 3. Violation of "The Alternatives Rule"
* **Severity:** 🔴 Blocker
* **Reviewer Lens:** `egm-structural`, `egm-design`, `egm-style`
* **Affected Scope:** `## Alternatives Weighed` (Lines 40–48)
* **Discrepancy Description:**
  1. **Header Naming Non-Compliance:** The standard mandates `## Alternatives Considered`; the document uses `## Alternatives Weighed`.
  2. **Improper Attribution:** Lines 43, 45, and 47 state "Rejected by architecture plan" and "Rejected by author", violating the rule that rejections must present concrete technical trade-offs without attributing rejections to individuals or circular self-references without links.
  3. **Lack of Comparative Structure:** Alternatives are listed as loose bullets rather than structured comparison nodes detailing the alternative, trade-offs, and concrete technical rationale.
* **Rationale & Recommendation:**
  Rename to `## Alternatives Considered`. Replace subjective attributions with objective technical trade-offs (e.g., memory overhead, operational complexity, network latency), and link directly to relevant architectural documents ([`cs_raiders_authentication_session_architecture_plan.md`](file:///mnt/public_storage/material/General/General/CS-raider/Project/cs_raiders_authentication_session_architecture_plan.md)).

---

### 4. In-Memory Rate Limiter Bypass in Multi-Replica Deployments & Argon2id CPU DoS
* **Severity:** 🟡 Major
* **Reviewer Lens:** `egm-design`
* **Affected Scope:** Lines 17–18, 27
* **Discrepancy Description:**
  Argon2id ($m=19456\text{ KiB}, t=2, p=1$) consumes 30–50ms+ of dedicated CPU time per hash. Relying solely on in-process `RateLimiter` partitions allows attackers to bypass limits by distributing requests across $N$ container replicas, causing widespread CPU saturation and thread pool starvation during timing-safe anti-enumeration dummy verification.
* **Rationale & Recommendation:**
  Document multi-replica rate limiting (ingress/reverse proxy rate limits via NGINX/Envoy/Cloudflare) and constrain cryptographic hashing via a bounded concurrency semaphore matched to available physical CPU cores.

---

### 5. Advisory Lock Contention, Hash Collisions & Role Reconciliation Deadlocks
* **Severity:** 🟡 Major
* **Reviewer Lens:** `egm-design`
* **Affected Scope:** Lines 15–16, 28, 32
* **Discrepancy Description:**
  `pg_advisory_xact_lock` on the 64-bit hash of a user's UUID carries a non-zero collision risk across large user bases, causing unrelated users to block each other during login. Furthermore, executing batch live session reconciliations across hundreds of users in a single transaction risks lock acquisition inversion, statement timeouts, and deadlocks with concurrent user logins.
* **Rationale & Recommendation:**
  Define bounded statement lock timeouts (`SET LOCAL lock_timeout = '2s'`), partition reconciliations into asynchronous batches, and isolate single-session concurrency checks to atomic SQL CTE statements under `uq_sessions_one_active`.

---

### 6. Omission of CSRF Defense Mechanism for Cookie-Based Refresh Tokens
* **Severity:** 🟡 Major
* **Reviewer Lens:** `egm-design`
* **Affected Scope:** Lines 20, 29, 34
* **Discrepancy Description:**
  The brief specifies 30-day rotating refresh tokens in `SameSite=Lax` cookies, but omits CSRF defenses for the `/api/v1/auth/refresh` and `/api/v1/auth/logout` endpoints, which is insufficient for cross-origin SPA topologies or top-level navigation vectors.
* **Rationale & Recommendation:**
  Explicitly specify double-submit CSRF protection (`X-CSRF-Token` header validated against an anti-forgery cookie) and strict `Origin`/`Referer` header validation.

---

### 7. Hyperbolic Absolutes, Fluffy Adverbs, and Promotional Buzzwords
* **Severity:** 🟡 Major
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** Lines 8, 13–14, 18, 20
* **Discrepancy Description:**
  The text contains ungrounded buzzwords ("hardened"), promotional adverbs ("rigorously enforced"), conversational intensifiers ("every single", "easily", "pure", "outright"), and hyperbolic absolutes ("guarantees zero stale sessions", "transforms PostgreSQL into the primary throughput bottleneck").
* **Rationale & Recommendation:**
  Calibrate to dry, objective engineering prose stating factual mechanisms and technical requirements directly.

---

### 8. Heading Asymmetry in Analysis & Outline Hierarchy Violations
* **Severity:** 🟡 Major
* **Reviewer Lens:** `egm-structural`
* **Affected Scope:** Lines 10–21, 22–39
* **Discrepancy Description:**
  `## Analysis` contains exactly one orphan subsection (`### Vulnerability & Risk Points`). Scope items in `## Goals vs. Non-Goals` are formatted as bullet list prefixes (`* **In-Scope Goals:**`) rather than standard H3 sub-headings (`### In-Scope Goals`, `### Out-of-Scope Non-Goals`).
* **Rationale & Recommendation:**
  Flatten or symmetrically balance subsections with `### Risks & Mitigations`. Convert bullet list headers to proper H3 headings.

---

### 9. Cross-Node Caching Asymmetry (Sessions vs. RBAC Permissions)
* **Severity:** 🟢 Nit
* **Reviewer Lens:** `egm-design`
* **Affected Scope:** Lines 30–31
* **Discrepancy Description:**
  The design enforces 0-second staleness for session revocation but permits 30-second stale permissions in `IMemoryCache`, leading to an inconsistent security posture during role/permission revocations.
* **Rationale & Recommendation:**
  Document the threat model justification or add a PostgreSQL `LISTEN/NOTIFY` channel for instant cache invalidation on role/user permission edits.

---

### 10. Unspecified Startup Configuration & Secret Validation
* **Severity:** 🟢 Nit
* **Reviewer Lens:** `egm-design`
* **Affected Scope:** Line 51
* **Discrepancy Description:**
  Phase 1 does not specify startup verification of entropy for secrets (`Auth:PasswordPepper`) or identity database seeding. Booting with missing or default secrets compromises password storage.
* **Rationale & Recommendation:**
  Add explicit startup configuration validation and seeder integration to Phase 1.

---

### 11. Missing Hyperlinks to Authoritative Source Files
* **Severity:** 🟢 Nit
* **Reviewer Lens:** `egm-structural`
* **Affected Scope:** Document-wide
* **Discrepancy Description:**
  Mentions of "architecture plan" and external standards lack markdown hyperlinks.
* **Rationale & Recommendation:**
  Add markdown links to [`cs_raiders_authentication_session_architecture_plan.md`](file:///mnt/public_storage/material/General/General/CS-raider/Project/cs_raiders_authentication_session_architecture_plan.md) and related codebase artifacts.
