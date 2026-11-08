# Interfaces

## Overview
Provides core application contracts and database abstractions, including the primary database context interface and repository specifications.

## Interface
- `[IApplicationDbContext.cs:IApplicationDbContext]`: Primary database context abstraction exposing entity `DbSet` collections and unit-of-work persistence via `SaveChangesAsync`.
- [Repositories](./Repositories/README.md): Entity repository interfaces for domain entities and access control.

## Invariants
- `[IApplicationDbContext.cs:SaveChangesAsync]`: Must commit tracked database entity changes atomically or roll back on error.

## Side Effects
- `[IApplicationDbContext.cs:SaveChangesAsync]`: Flushes tracked entity mutations to the underlying relational database.

## Verification
- Run: `dotnet test`
