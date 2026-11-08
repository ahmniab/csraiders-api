# Migrations

## Overview
Houses Entity Framework Core database migration definitions and model snapshots for PostgreSQL schema evolution, managing table creation, index setups, and extension installations for authentication and identity persistence.

## Interface
- `[20261105144818_Auth_Initial.cs:Auth_Initial]` - Initial authentication and identity schema migration.
- `[20261105144818_Auth_Initial.cs:Auth_Initial.Up]` - Executes DDL statements to enable `citext` extension, create identity and session tables, check constraints, foreign keys, and indexes.
- `[20261105144818_Auth_Initial.cs:Auth_Initial.Down]` - Reverts DDL statements by dropping database tables in reverse dependency order.
- `[AppDbContextModelSnapshot.cs:AppDbContextModelSnapshot]` - EF Core model snapshot representing the current database schema state.
- `[AppDbContextModelSnapshot.cs:AppDbContextModelSnapshot.BuildModel]` - Builds complete relational model metadata corresponding to the database snapshot.

## Invariants
- `[20261105144818_Auth_Initial.cs:Auth_Initial.Up]` - Target PostgreSQL database must support the `citext` extension.
- `[20261105144818_Auth_Initial.cs:Auth_Initial.Down]` - Teardown drops dependent tables first to preserve foreign key constraint integrity.

## Side Effects
- `[20261105144818_Auth_Initial.cs:Auth_Initial.Up]` - Executes DDL operations modifying PostgreSQL schema structure, creating tables, constraints, and indexes.
- `[20261105144818_Auth_Initial.cs:Auth_Initial.Down]` - Executes DDL operations dropping schema tables from the PostgreSQL database.

## Verification
- Run: `dotnet test`
