# Users

## Overview
Implements administrative commands and handlers for user account lifecycle management, including account suspension, active session invalidation, and account reactivation.

## Interface
- `[SuspensionCommands.cs:SuspendUserCommand]`: Mediator command to suspend a user account, initiating immediate session revocation and cache eviction.
- `[SuspensionCommands.cs:ReactivateUserCommand]`: Mediator command to restore a suspended user account to active status.
- `[SuspensionCommands.cs:SuspensionResult]`: Outcome record indicating success, not found (`Missing()`), or invalid state transition (`Invalid()`).
- `[SuspensionCommands.cs:SuspendUserCommandHandler]`: Command handler enforcing suspension state transitions, revoking sessions, and enqueueing cache eviction.
- `[SuspensionCommands.cs:ReactivateUserCommandHandler]`: Command handler restoring suspended user accounts and logging audit entries.

## Invariants
- `[SuspensionCommands.cs:SuspendUserCommandHandler]`: Rejects suspension request if user is not found, or user status is already `Suspended` or `Deleted`.
- `[SuspensionCommands.cs:ReactivateUserCommandHandler]`: Rejects reactivation request if user is not found, or user status is not `Suspended`.

## Side Effects
- `[SuspensionCommands.cs:SuspendUserCommandHandler]`: Mutates user entity status to `Suspended`, revokes all active user sessions in database, schedules session cache eviction post-commit, and writes `account_suspended` audit log.
- `[SuspensionCommands.cs:ReactivateUserCommandHandler]`: Mutates user entity status to `Active` and writes `account_reactivated` audit log.

## Verification
- Run: `dotnet test`
