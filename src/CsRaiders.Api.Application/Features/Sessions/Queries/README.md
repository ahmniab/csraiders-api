# Queries

## Overview
Provides query handling for retrieving active session summaries and metadata for a specific user.

## Interface
- `[GetActiveSessionsQuery.cs:GetSessionSummariesQuery]`: Mediator query requesting active session summaries for `UserId`.
- `[GetActiveSessionsQuery.cs:GetSessionSummariesQueryHandler]`: Query handler retrieving session summaries via `ISessionRepository`.
- `[GetSessionStatusByIdQueryHandler.cs:GetSessionStatusByIdQuery]`: Mediator query resolving current `SessionStatus` for a session id, first from `IDistributedCache`, then from `ISessionRepository`.

## Invariants
- `[GetActiveSessionsQuery.cs:GetSessionSummariesQueryHandler]`: Evaluates session active status against current timestamp `clock.UtcNow`.
- Both query records implement `IReadOnlyMessage` and therefore run without a database transaction and without `SaveChanges` (handlers never persist via the `DbContext`; the `IDistributedCache` write in `GetSessionStatusByIdQueryHandler` is external to the database transaction).

## Side Effects
- None

## Verification
- Run: `dotnet test`
