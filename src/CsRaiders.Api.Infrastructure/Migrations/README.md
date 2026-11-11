# Migrations

## Overview
Houses Entity Framework Core database migration definitions and model snapshots for PostgreSQL schema evolution, managing table creation, index setups, foreign key relationships, check constraints, and extension installations for authentication, authorization, and session persistence.

## Interface
- `[20261109133710_Auth_Initial.cs:Auth_Initial]` - Initial authentication, authorization, and session management schema migration.
- `[20261109133710_Auth_Initial.cs:Auth_Initial.Up]` - Executes DDL statements to enable `citext` extension, create database tables (`app_settings`, `pending_registrations`, `roles`, `users`, `user_roles`, `user_permissions`, `sessions`, `security_events`, `login_attempts`, `password_reset_tokens`), check constraints, foreign keys, and indexes.
- `[20261109133710_Auth_Initial.cs:Auth_Initial.Down]` - Reverts DDL statements by dropping database tables in reverse dependency order.
- `[AppDbContextModelSnapshot.cs:AppDbContextModelSnapshot]` - EF Core model snapshot representing the current database schema state.
- `[AppDbContextModelSnapshot.cs:AppDbContextModelSnapshot.BuildModel]` - Builds complete relational model metadata corresponding to the database snapshot.

## Invariants
- `[20261109133710_Auth_Initial.cs:Auth_Initial.Up]` - Target PostgreSQL database must support the `citext` extension.
- `[20261109133710_Auth_Initial.cs:Auth_Initial.Down]` - Teardown drops dependent tables first to preserve foreign key constraint integrity.

## Side Effects
- `[20261109133710_Auth_Initial.cs:Auth_Initial.Up]` - Executes DDL operations modifying PostgreSQL schema structure, creating tables, constraints, and indexes.
- `[20261109133710_Auth_Initial.cs:Auth_Initial.Down]` - Executes DDL operations dropping schema tables from the PostgreSQL database.

## Verification
- Run: `dotnet test`
