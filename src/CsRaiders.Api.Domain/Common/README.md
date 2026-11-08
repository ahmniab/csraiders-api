# Common

## Overview
Common domain primitives providing base entity abstractions and identifier generation strategies.

## Interface
- [Entity.cs:Entity]: Abstract base class providing primary key identity and optimistic concurrency version tracking.
- [Entity.cs:Entity.Id]: Unique identifier initialized upon entity instantiation via `IdGenerator.New()`.
- [Entity.cs:Entity.Version]: Concurrency token mapped to PostgreSQL `xmin` for optimistic concurrency checking.
- [IdGenerator.cs:IdGenerator]: Static utility for generating sequential, time-ordered UUIDv7 identifiers.
- [IdGenerator.cs:IdGenerator.New]: Method producing a new UUIDv7 identifier (`Guid.CreateVersion7()`) for improved database B-tree index locality.

## Invariants
- [Entity.cs:Entity.Id]: Initialized upon creation using `IdGenerator.New()` and must not be empty.
- [Entity.cs:Entity.Version]: Managed as an optimistic concurrency token reflecting PostgreSQL `xmin`; private setter prevents manual modification outside persistence mapping.

## Side Effects
- [IdGenerator.cs:IdGenerator.New]: Generates a timestamped UUIDv7 relying on system time.

## Verification
- Run: `dotnet test`
