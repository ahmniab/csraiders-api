# API

## Overview
Entry point and hosting orchestration layer for the CsRaiders API service. Configures dependency injection, registers application and infrastructure services, manages HTTP request middleware, executes development migrations and seeding, and exposes HTTP endpoints.

## Interface
- Top-level application entry point (`[Program.cs:Program]`)
- Subdirectories:
  - [`./Middleware/README.md`](./Middleware/README.md)

## Invariants
- `GlobalExceptionMiddleware` is registered as the first middleware in the HTTP request processing pipeline (`[Program.cs:app.UseMiddleware<GlobalExceptionMiddleware>]`).
- Database migrations (`AppDbContext.Database.MigrateAsync`) and identity seeding (`IdentitySeeder.SeedAsync`) execute only when running in the Development environment (`[Program.cs:app.Environment.IsDevelopment]`).
- Identity seeding during development explicitly disallows concurrent multi-sessions for students (`[Program.cs:IdentitySeeder.SeedAsync]`).
- Request authorization and HTTPS redirection middleware are enforced prior to routing controller endpoints (`[Program.cs:app.UseHttpsRedirection]`, `[Program.cs:app.UseAuthorization]`, `[Program.cs:app.MapControllers]`).

## Side Effects
- Binds to network interfaces and starts listening for incoming HTTP/HTTPS requests (`[Program.cs:app.Run]`).
- Applies pending database migrations via Entity Framework Core during development startup (`[Program.cs:AppDbContext.Database.MigrateAsync]`).
- Seeds default identity and authorization records in the persistence store during development startup (`[Program.cs:IdentitySeeder.SeedAsync]`).

## Verification
- Run: `dotnet test`
