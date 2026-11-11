# Queries

## Overview
Provides query handling for retrieving active session summaries and metadata for a specific user.

## Interface
- `[GetActiveSessionsQuery.cs:GetSessionSummariesQuery]`: Mediator query requesting active session summaries for `UserId`.
- `[GetActiveSessionsQuery.cs:GetSessionSummariesQueryHandler]`: Query handler retrieving session summaries via `ISessionRepository`.

## Invariants
- `[GetActiveSessionsQuery.cs:GetSessionSummariesQueryHandler]`: Evaluates session active status against current timestamp `clock.UtcNow`.

## Side Effects
- None

## Verification
- Run: `dotnet test`
