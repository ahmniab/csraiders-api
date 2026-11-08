# CsRaiders.Api Blueprint

## System Overview
CsRaiders.Api is a .NET Web API backend following Clean Architecture principles. It provides identity, authentication, session management, role-based access control (RBAC), and brute-force throttling for the CsRaiders platform. The system is partitioned into API presentation and hosting, Application use-case contracts and repositories, Domain business rules and entities, and Infrastructure PostgreSQL persistence using Entity Framework Core.

## Subdirectories & System Boundaries
- src: [Source Tree Root](./src/README.md) - Contains the four Clean Architecture subsystem projects (API, Application, Domain, and Infrastructure).

## Global Invariants
- Clean Architecture dependency rule: Domain has zero external package dependencies; Application depends strictly on Domain; Infrastructure and API depend on Application and Domain.
- Entity identifiers are generated in the domain layer using sequential UUIDv7 via `IdGenerator.New()` and are never database-generated.
- Optimistic concurrency control is enforced across entities mapped to PostgreSQL `xmin` via `Entity.Version`.
- Permission evaluation enforces that explicit `Deny` overrides always take precedence over role permissions and `Grant` overrides.
- Inactive or soft-deleted users (`DeletedAt != null` or `Status != UserStatus.Active`) are suppressed by query filters and barred from authenticating.
- Plaintext passwords are never persisted; all password hashes must adhere to the Argon2id PHC format (`$argon2id$v=19$`).
- Refresh token rotation verifies previous token hashes to detect reuse and revoke compromised sessions, enforcing single-session concurrency via the database unique index `uq_sessions_one_active` when enabled.

## Global Side Effects
- Connects to PostgreSQL, manages relational schemas via Entity Framework Core migrations, and seeds default roles and administrative accounts.
- Binds to network interfaces and listens for incoming HTTP/HTTPS requests.

## System Verification
- Run: `dotnet test`
