# Models

## Overview
Defines shared Data Transfer Objects (DTOs), operation results, projection summaries, and pagination query models used throughout the Application layer.

## Interface
- `[PagedResult.cs:PagedResult<T>]`: Generic keyset-paginated container holding `Items`, `NextCursor`, and `TotalReturned`.
- `[Result.cs:Result]`: Non-generic operation result capturing boolean `Succeeded` state and string error array `Errors`.
- `[Result.cs:Result<T>]`: Generic operation result encapsulating strongly typed payload `Value` and error states.
- `[SessionSummary.cs:SessionSummary]`: Projection record capturing session metadata, client device fingerprint, IP, and revocation state.
- `[UserFilter.cs:UserFilter]`: Filter criteria record supporting keyset pagination (`CursorId`, `Limit`), status, role, and search term queries.
- `[UserSummary.cs:UserSummary]`: Lightweight projection record capturing core user identity and contact attributes.

## Invariants
- `[Result.cs:Result.Success]`: `Succeeded` must be true with an empty `Errors` array.
- `[Result.cs:Result.Failure]`: `Succeeded` must be false containing one or more error descriptions.
- `[UserFilter.cs:UserFilter]`: Page size `Limit` defaults to 20 when not explicitly set.

## Side Effects
- None

## Verification
- Run: `dotnet test`
