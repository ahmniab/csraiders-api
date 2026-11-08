# src

## Overview
Source code root directory grouping the clean architecture subsystem projects for the CsRaiders API service, structured into API hosting, Application use cases & contracts, Domain business logic & entities, and Infrastructure persistence & external implementations.

## Subdirectories & Boundaries
- CsRaiders.Api.API: [API Blueprint](./CsRaiders.Api.API/README.md) - Entry point and hosting orchestration layer managing HTTP pipelines, middleware, dependency wiring, and endpoint routing.
- CsRaiders.Api.Application: [Application Blueprint](./CsRaiders.Api.Application/README.md) - Application layer encapsulating business contracts, persistence repository abstractions, data transfer models, and dependency container extensions.
- CsRaiders.Api.Domain: [Domain Blueprint](./CsRaiders.Api.Domain/README.md) - Core enterprise domain logic, aggregate roots, entities, value objects, domain rules, authorization policies, and session constraints.
- CsRaiders.Api.Infrastructure: [Infrastructure Blueprint](./CsRaiders.Api.Infrastructure/README.md) - External technical capabilities and relational database persistence using Entity Framework Core with PostgreSQL, including migrations, configurations, seeders, and repository implementations.
