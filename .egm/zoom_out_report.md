# Stage 5 Initial Zoom Out Review Report

**Target Document:** [`concepts_brief.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md) (Lines 1–55)  
**Standard Evaluated:** [Zoom-Out Standard](file:///home/ahmniab/.gemini/config/plugins/egm/resources/document_standards/zoom-out-standard.md)  
**Reviewers:** Skeptical Architect (`egm-zoom-out`) & Inventive Catalyst (`egm-inventive-catalyst`)

---

## Reviewer Status

| Reviewer | Status |
| :--- | :--- |
| `egm-zoom-out` | completed |
| `egm-inventive-catalyst` | completed |

---

## Prioritized Findings (Blockers -> Major -> Nits)

### 1. Hybrid Stateless JWT + Synchronous DB Session Lookup Anti-Pattern
* **Severity:** 🟡 Major
* **Reviewer Lens:** `egm-zoom-out` & `egm-inventive-catalyst`
* **Affected Scope:** `## Analysis` (Lines 13–14), `## Goals vs. Non-Goals` (Lines 29–31, 35), `## Alternatives Weighed` (Lines 42–45)
* **Discrepancy Description:** 
  The proposal mandates stateless JWT access tokens while simultaneously querying PostgreSQL on *every single authenticated HTTP request* (`SessionValidationMiddleware` consulting `sessions` table by `sid` with zero in-memory caching).
  1. **Dual Overhead:** JWTs exist for stateless signature verification without database trips. Paying both JWT verification CPU/payload costs and synchronous relational database roundtrips on every request combines the weaknesses of both models.
  2. **Inconsistent Revocation Invariant:** The architecture accepts a 30-second stale window via `IMemoryCache` for RBAC permissions, yet rejects any caching or sliding window for session revocation, concentrating peak traffic directly on PostgreSQL connection pools.
* **Inventive Catalyst & Architectural Resolution:**
  * **Option A (Beforehand Cushioning):** Introduce an in-process short-TTL cache (5–10 seconds in `IMemoryCache`) for active session status.
  * **Option B (Intermediary / Dynamic Invalidation):** Use PostgreSQL `LISTEN`/`NOTIFY` to broadcast `session_revoked` events, invalidating in-memory session status across nodes immediately while serving hot reads from cache.
  * **Option C (Minimalist Data Flow):** If zero caching is non-negotiable, replace the JWT access token with an opaque bearer token or session cookie to eliminate superfluous JWT encoding and signature verification overhead.

---

### 2. Lock Contention & Starvation via Open Transaction Advisory Locks
* **Severity:** 🟡 Major
* **Reviewer Lens:** `egm-inventive-catalyst`
* **Affected Scope:** `## Analysis` (Lines 15–16), `## Key Decisions & Staging for Next Steps` (Line 53)
* **Discrepancy Description:**
  The design wraps user logins in an exclusive transaction-scoped advisory lock (`pg_advisory_xact_lock`) across the entire login transaction to serialize single-session concurrency. Holding this lock across credential validation, re-hashing, token issuance, and audit writes risks worker thread starvation and connection hogging under burst login attempts.
* **Proposed Resolution (Pattern 2: Extraction):**
  Extract and isolate the single-session concurrency enforcement into an atomic SQL statement or CTE (`UPDATE ... RETURNING` / deactivating superseded sessions while inserting the new row) directly protected by the partial unique index `uq_sessions_one_active`, removing multi-step open transaction locks from the login path.

---

### 3. Ignoring Existing Codebase Assets in Project Staging
* **Severity:** 🟡 Major
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** `## Key Decisions & Staging for Next Steps` (Lines 51, 54)
* **Discrepancy Description:**
  The staging plan treats the database schema, entity configurations, and permission/session policies as unwritten greenfield work. In reality, these are already implemented in the repository:
  * Entities and EF Core configurations exist in `CsRaiders.Api.Infrastructure/Persistence/Configurations/SessionConfigurations.cs` and migrations in `20261105144818_Auth_Initial.cs`.
  * Domain policies exist in `EffectivePermissions.cs` and `SessionPolicy.cs`.
* **Rationale & Recommendation:**
  Update the staging plan to explicitly integrate and test existing domain and persistence components rather than scheduling redundant implementation work.

---

### 4. Unbounded CPU Exhaustion on Dummy Argon2id Verification
* **Severity:** 🟢 Nit
* **Reviewer Lens:** `egm-inventive-catalyst`
* **Affected Scope:** `## Analysis` (Lines 17–18), `## Goals vs. Non-Goals` (Line 27)
* **Discrepancy Description:**
  Executing an unthrottled dummy Argon2id hash ($19\text{ MiB}$, 2 iterations, 15–50ms CPU) on every invalid login to prevent timing-based user enumeration can exhaust container CPU during unauthenticated distributed bursts.
* **Proposed Resolution:**
  Throttle concurrent Argon2id hashing executions via a bounded concurrency semaphore matched to available CPU cores, combined with Layer 1 IP/email rate limiters.

---

### 5. YAGNI on Transparent Password Parameter Upgrades
* **Severity:** 🟢 Nit
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** `## Goals vs. Non-Goals` (Line 25)
* **Discrepancy Description:**
  Planning dynamic Argon2id parameter upgrade logic on login adds conditional code paths for a greenfield system with no legacy password hashes.
* **Rationale & Recommendation:**
  Defer parameter migration logic until hashing configuration is modified in a future milestone.

---

### 6. Premature Environment-Forked Cookie Logic
* **Severity:** 🟢 Nit
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** `## Analysis` (Lines 19–20)
* **Discrepancy Description:**
  Bifurcating cookie configuration between environments (`__Host-` in production vs standard cookies in development) introduces environment-specific code paths.
* **Rationale & Recommendation:**
  Enforce local HTTPS in development via standard ASP.NET Core developer certificates (`dotnet dev-certs https`), preserving production cookie parity.

---

### 7. Altitude Drop into Concrete Helper Names
* **Severity:** 🟢 Nit
* **Reviewer Lens:** `egm-zoom-out`
* **Affected Scope:** `## Key Decisions & Staging for Next Steps` (Line 51)
* **Discrepancy Description:**
  Listing concrete class names (`Argon2idPasswordHasher`, `HmacOtpGenerator`, `Sha256TokenHasher`) in high-level staging drops into implementation weeds.
* **Rationale & Recommendation:**
  Reference high-level capabilities (Cryptographic Services) instead of concrete implementation classes.
