# CsRaiders.Api.Infrastructure

## Overview
The infrastructure layer provides external technical capabilities, database persistence, and service wiring for the CsRaiders API. It encapsulates Entity Framework Core relational mappings, PostgreSQL persistence, migrations, concrete repository implementations, identity seeding, and dependency injection container configuration.

## Subdirectories & Boundaries
- DependencyInjection: [./DependencyInjection/README.md](./DependencyInjection/README.md) - Service collection extensions registering infrastructure dependencies, DbContext, system clock providers, seeders, and repositories.
- Persistence: [./Persistence/README.md](./Persistence/README.md) - Relational database persistence using Entity Framework Core and PostgreSQL, including DbContext, value converters, identity seeding, entity configurations, migrations, and repositories.
