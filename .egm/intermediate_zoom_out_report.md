# Stage 8 Intermediate Zoom Out Review Report

**Target Document:** [`concepts_brief.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md) (Lines 1–55)  
**Standard Evaluated:** [Zoom-Out Standard](file:///home/ahmniab/.gemini/config/plugins/egm/resources/document_standards/zoom-out-standard.md)  
**Reviewer:** Skeptical Architect (`egm-zoom-out`)  
**Context Considered:** [`cs_raiders_authentication_session_architecture_plan.md`](file:///mnt/public_storage/material/General/General/CS-raider/Project/cs_raiders_authentication_session_architecture_plan.md), [`zoom_out_report.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/zoom_out_report.md), [`critic_report.md`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/critic_report.md)

---

## Reviewer Status

| Reviewer | Status |
| :--- | :--- |
| `egm-zoom-out` | completed |

---

## Prioritized Findings (Blockers -> Major -> Nits)

### 1. Architectural Incoherence: Hybrid Stateless JWT with Synchronous Per-Request DB Validation
* **Severity:** 🔴 Blocker
* **Affected Scope:** `## Overview` ([Line 8](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L8)), `## Analysis` ([Lines 13–14](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L13-L14)), `## Goals vs. Non-Goals` ([Lines 29–31](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L29-L31)), `## Alternatives Considered` ([Lines 42–45](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L42-L45))
* **Simplicity Rationale:**
  The design pairs stateless 15-minute JWT access tokens with a mandatory, synchronous relational query against PostgreSQL on *every single authenticated HTTP request* via `SessionValidationMiddleware`.
  JWTs were created specifically to avoid database roundtrips via cryptographic signature verification. Enforcing an unbuffered PostgreSQL query per request negates the throughput benefit of JWTs while retaining CPU and header size overhead:
  1. **Superfluous Overhead:** The server executes cryptographic signature verification and transfers large Bearer headers, but throughput remains strictly throttled by PostgreSQL connection pools and query latency.
  2. **Inconsistent Invariant:** The architecture accepts a 30-second stale window via `IMemoryCache` for user permissions, but rejects even a 5–10 second sliding cache for session validity.
* **Recommendation:**
  Either introduce an in-process sliding cache (5–10 seconds in `IMemoryCache` with optional PostgreSQL `LISTEN`/`NOTIFY` invalidation) to cushion database load, or abandon JWTs in favor of opaque session tokens if synchronous per-request database validation is mandatory.

---

### 2. Context Insufficiency & Missing Root Problem Statement
* **Severity:** 🔴 Blocker
* **Affected Scope:** `## Overview` ([Lines 6–9](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L6-L9)), Document Structure
* **Simplicity Rationale:**
  Under Section 0 ("Context Sufficiency") and Section 1 ("Root Problem Focus") of the Zoom-Out Standard, an architectural specification must articulate the core problem being solved and justify custom engineering. The brief dives straight into mechanics without explaining:
  1. Why standard platform frameworks (such as ASP.NET Core Identity, OpenIddict, or Duende) were rejected in favor of building a custom authentication subsystem.
  2. The specific business drivers necessitating bespoke session concurrency and revocation rules.
* **Recommendation:**
  Add a dedicated `## Problem Statement` outlining the business drivers, compliance mandates (e.g., OWASP ASVS v4.0 L2), and security justifications.

---

### 3. Reinventing Existing Codebase Assets in Project Staging
* **Severity:** 🟡 Major
* **Affected Scope:** `## Key Decisions & Staging for Next Steps` ([Lines 51, 54](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L51-L54))
* **Simplicity Rationale:**
  Section 4 ("Leverage Existing") mandates reusing established components instead of scheduling redundant greenfield work. The staging section plans the creation of database migrations, schema constraints, and the permission engine as future work. A code search reveals these components are already written and committed in the repository:
  * **EF Core Schema & Constraints:** [`SessionConfiguration`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Infrastructure/Persistence/Configurations/SessionConfigurations.cs#L7-L32) and [`IdentityConfigurations`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Infrastructure/Persistence/Configurations/IdentityConfigurations.cs) already define table mappings, check constraints, and the partial unique index [`uq_sessions_one_active`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Infrastructure/Persistence/Configurations/SessionConfigurations.cs#L23-L25).
  * **Database Migration:** [`20261105144818_Auth_Initial.cs`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Infrastructure/Persistence/Migrations/20261105144818_Auth_Initial.cs) is already generated.
  * **Permission Evaluation Engine:** [`EffectivePermissions.Resolve`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Policies/EffectivePermissions.cs#L6-L19) already implements `(Roles ∪ Grants) \ Denies`.
  * **Session Concurrency Logic:** [`SessionPolicy.Decide`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Policies/SessionPolicy.cs#L7-L26) already implements single-session and concurrent session decision logic.
  * **Domain Entities & Catalogs:** [`PermissionCatalog`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Constants/PermissionCatalog.cs), [`RolePresets`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Constants/RolePresets.cs), [`Session`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Entities/Session.cs), and [`User`](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/src/CsRaiders.Api.Domain/Entities/User.cs) already exist.
* **Recommendation:**
  Update staging to explicitly integrate, configure, and test these existing assets rather than re-implementing them.

---

### 4. Over-Engineered Concurrency via Open Transaction Advisory Locks
* **Severity:** 🟡 Major
* **Affected Scope:** `## Analysis` ([Lines 15–16](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L15-L16)), `## Goals vs. Non-Goals` ([Line 28](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L28)), `## Key Decisions & Staging for Next Steps` ([Line 53](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L53))
* **Simplicity Rationale:**
  Wrapping login and session reconciliation inside explicit PostgreSQL transaction-level advisory locks (`pg_advisory_xact_lock`) on the user UUID serializes logins and ties up database connections across multi-step operations.
  Exclusivity can be achieved via an atomic SQL CTE deactivating superseded sessions while inserting the new row under `uq_sessions_one_active`, eliminating advisory lock contention and hash collision risks.

---

### 5. YAGNI: Speculative Password Parameter Upgrade Logic
* **Severity:** 🟢 Nit
* **Affected Scope:** `## Goals vs. Non-Goals` ([Line 25](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L25))
* **Simplicity Rationale:**
  Specifying transparent parameter upgrade logic on login in a greenfield Milestone 0 deployment is speculative over-engineering. There are no legacy hashes in the database.
* **Recommendation:**
  Defer parameter migration logic until hashing configuration is modified in a future milestone.

---

### 6. Premature Environment-Branching for Cookie Configuration
* **Severity:** 🟢 Nit
* **Affected Scope:** `## Analysis` ([Lines 19–20](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L19-L20))
* **Simplicity Rationale:**
  Branching cookie configuration between production (`__Host-csraiders_rt`) and development (`csraiders_rt`) creates unnecessary environment divergence. ASP.NET Core natively supports local HTTPS development via `dotnet dev-certs https`.
* **Recommendation:**
  Enforce local HTTPS in development to preserve cookie parity.

---

### 7. Altitude Drop into Low-Level Implementation Class Names
* **Severity:** 🟢 Nit
* **Affected Scope:** `## Key Decisions & Staging for Next Steps` ([Line 51](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/concepts_brief.md#L51))
* **Simplicity Rationale:**
  Listing concrete class names in the high-level conceptual brief violates Section 3 ("Anti-Weeds") of the Zoom-Out Standard.
* **Recommendation:**
  Reference high-level capabilities (Cryptographic Services) instead of concrete class breakdown.
