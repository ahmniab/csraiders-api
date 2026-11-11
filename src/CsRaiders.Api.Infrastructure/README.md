# CsRaiders.Api.Infrastructure

## Overview
The infrastructure layer provides external technical capabilities, database persistence, cryptographic operations, and service wiring for the CsRaiders API. It encapsulates Entity Framework Core relational mappings, PostgreSQL persistence, migrations, concrete repository implementations, identity seeding, cryptographic security providers, background workers, and dependency injection container configuration.

## Subdirectories & Boundaries
- DependencyInjection: [./DependencyInjection/README.md](./DependencyInjection/README.md) - Service collection extensions registering infrastructure dependencies, DbContext factory, system clock providers, seeders, repositories, security providers, and background workers.
- Interceptors: [./Interceptors/README.md](./Interceptors/README.md) - Entity Framework Core save-changes interceptors providing centralized redaction of sensitive credential properties in audit payloads.
- Migrations: [./Migrations/README.md](./Migrations/README.md) - Database migration history and model snapshot managing PostgreSQL schema evolution, tables, constraints, and indexes.
- Persistence: [./Persistence/README.md](./Persistence/README.md) - Relational database persistence using Entity Framework Core and PostgreSQL, including DbContext, value converters, transaction scopes, identity seeding, entity configurations, and concrete repositories.
- Security: [./Security/README.md](./Security/README.md) - Cryptographic security services providing Argon2id password hashing, HMAC-SHA256 one-time password generation/verification, and JWT token issuance and validation.
- Services: [./Services/README.md](./Services/README.md) - Background hosted workers for auth cleanup and email sending, login attempt tracking, metric recorders, security audit writers, and session reconciliation services.
