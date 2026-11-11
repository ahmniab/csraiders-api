# API

## Overview
Entry point and hosting orchestration layer for the CsRaiders API service. Configures dependency injection, registers application and infrastructure services, manages HTTP request middleware, executes development migrations and seeding, and exposes HTTP endpoints.

## Interface
- Top-level application entry point and host bootstrap (`[Program.cs:Run]`)
- Subdirectories:
  - [`./Authorization/README.md`](./Authorization/README.md)
  - [`./Common/README.md`](./Common/README.md)
  - [`./Controllers/README.md`](./Controllers/README.md)
  - [`./Extensions/README.md`](./Extensions/README.md)
  - [`./Filters/README.md`](./Filters/README.md)
  - [`./Middleware/README.md`](./Middleware/README.md)
  - [`./Services/README.md`](./Services/README.md)

## Invariants
- `GlobalExceptionMiddleware` is registered as the primary error handling middleware in the HTTP request processing pipeline (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).
- Database migrations (`AppDbContext.Database.MigrateAsync`) and identity seeding (`IdentitySeeder.SeedAsync`) execute only when running in the Development environment (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).
- Identity seeding during development explicitly disallows concurrent multi-sessions for students (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).
- Request authorization, session validation, and HTTPS redirection middleware are enforced prior to routing controller endpoints (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).
- Host initialization invokes `AddCsRaidersApi` on the builder and `UseCsRaidersApi` on the application prior to running the host (`[Program.cs:Run]`).

## Side Effects
- Binds to network interfaces and starts listening for incoming HTTP/HTTPS requests (`[Program.cs:Run]`).
- Applies pending database migrations via Entity Framework Core during development startup (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).
- Seeds default identity and authorization records in the persistence store during development startup (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).

## Verification
- Run: `dotnet test`
