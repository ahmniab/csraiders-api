# src

## Overview
Source code root directory grouping the clean architecture subsystem projects for the CsRaiders API service, structured into API hosting, Application use cases & contracts, Domain business logic & entities, and Infrastructure persistence & external implementations.

## Subdirectories & Boundaries
- CsRaiders.Api.API: [./CsRaiders.Api.API/README.md](./CsRaiders.Api.API/README.md) - Entry point and hosting orchestration layer configuring DI, middleware, database migration seeding, and HTTP endpoint routing.
- CsRaiders.Api.Application: [./CsRaiders.Api.Application/README.md](./CsRaiders.Api.Application/README.md) - Application core layer orchestrating CQRS use cases, commands, queries, common abstractions, and repository contracts.
- CsRaiders.Api.Domain: [./CsRaiders.Api.Domain/README.md](./CsRaiders.Api.Domain/README.md) - Core enterprise domain logic, aggregate roots, entities, value objects, domain rules, authorization policies, and session constraints.
- CsRaiders.Api.Infrastructure: [./CsRaiders.Api.Infrastructure/README.md](./CsRaiders.Api.Infrastructure/README.md) - Infrastructure layer providing PostgreSQL persistence via Entity Framework Core, cryptographic operations, background workers, and service implementations.
