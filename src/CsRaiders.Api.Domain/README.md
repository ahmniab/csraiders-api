# Domain

## Overview
Domain layer containing core business logic, entities, value objects, domain rules, authorization policies, and session management for the CsRaiders API.

## Subdirectories & Boundaries
- Common: [./Common/README.md](./Common/README.md) - Base entity abstractions and sequential UUIDv7 identifier generation.
- Constants: [./Constants/README.md](./Constants/README.md) - System authentication rules, permission catalogs, and role presets.
- Entities: [./Entities/README.md](./Entities/README.md) - Aggregate roots and domain entities for users, sessions, roles, OTP registration, and login attempts.
- Enums: [./Enums/README.md](./Enums/README.md) - Domain-wide enum types for statuses, scopes, revocation reasons, and authentication outcomes.
- Exceptions: [./Exceptions/README.md](./Exceptions/README.md) - Domain exception definitions with structured error codes.
- Policies: [./Policies/README.md](./Policies/README.md) - Business authorization resolution and session concurrency policy evaluation.
- ValueObjects: [./ValueObjects/README.md](./ValueObjects/README.md) - Value objects and credential guards for email normalization and password hash enforcement.
