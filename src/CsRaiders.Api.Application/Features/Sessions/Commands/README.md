# Commands

## Overview
Implements session termination commands, including individual session revocation, user logout, and bulk revocation of concurrent active sessions.

## Interface
- `[RevokeSessionCommands.cs:RevokeSessionCommand]`: Mediator command with `UserId` and `SessionId` to revoke a specific session.
- `[RevokeSessionCommands.cs:RevokeAllOtherSessionsCommand]`: Mediator command with `UserId` and `CurrentSessionId` to revoke all other active sessions.
- `[RevokeSessionCommands.cs:LogoutCommand]`: Mediator command to log out a user by revoking the current session.
- `[RevokeSessionCommands.cs:RevokeSessionResult]`: Result record containing boolean `Revoked` status.
- `[RevokeSessionCommands.cs:RevokeAllSessionsResult]`: Result record containing `RevokedCount`.
- `[RevokeSessionCommands.cs:RevokeSessionCommandHandler]`: Handler validating session ownership and revoking target session.
- `[RevokeSessionCommands.cs:LogoutCommandHandler]`: Handler revoking session upon user logout.
- `[RevokeSessionCommands.cs:RevokeAllOtherSessionsCommandHandler]`: Handler performing bulk revocation of other active sessions while preserving the current session.

## Invariants
- `[RevokeSessionCommands.cs:RevokeSessionCommandHandler]`: Session must belong to the requesting user (`UserId`) and currently be active (`IsActive`).
- `[RevokeSessionCommands.cs:RevokeAllOtherSessionsCommandHandler]`: Must exclude `CurrentSessionId` from bulk revocation.

## Side Effects
- `[RevokeSessionCommands.cs:RevokeSessionCommandHandler]`: Mutates session revocation status in database, evicts session from cache, and logs `session_revoked` audit event.
- `[RevokeSessionCommands.cs:LogoutCommandHandler]`: Mutates session revocation status in database, evicts session from cache, and logs `session_revoked` audit event.
- `[RevokeSessionCommands.cs:RevokeAllOtherSessionsCommandHandler]`: Bulk updates session revocation status in database, evicts all affected sessions from cache, and logs `session_revoked` audit event.

## Verification
- Run: `dotnet test`
