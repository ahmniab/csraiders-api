# Persistence

## Overview
Implements relational persistence for CsRaiders.Api using Entity Framework Core with Npgsql PostgreSQL. Encapsulates the core database context, custom value converters for value objects and enums, identity database seeding, entity model configurations, schema migrations, and repository implementations.

## Interface
- `[AppDbContext.cs:AppDbContext]` - Primary Entity Framework Core database context implementing `IApplicationDbContext`, exposing `DbSet<T>` for users, roles, user roles, user permissions, sessions, login attempts, pending registrations, and password reset tokens.
- `[Converters.cs:EmailConverter]` - EF Core value converter mapping `Email` value object to PostgreSQL `citext` column.
- `[Converters.cs:SnakeCaseEnumConverter]` - Generic EF Core value converter converting enum values to snake_case database strings.
- `[IdentitySeeder.cs:IdentitySeeder]` - Idempotent database seeder creating initial system role presets.
- Subdirectories:
  - [Configurations](./Configurations/README.md) - Entity configuration mappings and check constraints.
  - [Migrations](./Migrations/README.md) - Schema migration history and model snapshot.
  - [Repositories](./Repositories/README.md) - Concrete repository implementations for domain aggregates.

## Invariants
- `[AppDbContext.cs:AppDbContext.OnModelCreating]` - Applies all entity configurations dynamically from the assembly via `ApplyConfigurationsFromAssembly`.
- `[AppDbContext.cs:AppDbContext.ConfigureConventions]` - Applies global column conventions: `Email` maps to `citext`, and domain enums map to snake_case strings.
- `[IdentitySeeder.cs:IdentitySeeder.SeedAsync]` - Prevents duplicate role seeding by filtering out existing role slugs.
- `[IdentitySeeder.cs:IdentitySeeder.SeedAsync]` - Omits `SessionMulti` permission on student-scoped roles unless `allowSessionMultiForStudents` is true.

## Side Effects
- `[IdentitySeeder.cs:IdentitySeeder.SeedAsync]` - Queries and writes missing system role records to the PostgreSQL database via `SaveChangesAsync`.
- `[AppDbContext.cs:AppDbContext]` - Manages database connections, executes queries, tracks entity states, and commits SQL transactions against PostgreSQL.

## Verification
- Run: `dotnet test`
