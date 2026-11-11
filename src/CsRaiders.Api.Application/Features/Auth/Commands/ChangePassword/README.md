# ChangePassword

## Overview
Handles user password changes, validating existing credentials, applying password complexity rules, hashing new passwords, and invalidating concurrent active sessions.

## Interface
- `[ChangePasswordCommand.cs:ChangePasswordCommand]`: Mediator command with `UserId`, `CurrentSessionId`, `CurrentPassword`, and `NewPassword`.
- `[ChangePasswordCommand.cs:ChangePasswordResult]`: Result record containing `Succeeded`, `CapacityExhausted`, and `ErrorCode`.
- `[ChangePasswordCommand.cs:ChangePasswordCommandValidator]`: FluentValidation validator enforcing password presence and length constraints (8-128 characters).
- `[ChangePasswordCommand.cs:ChangePasswordCommandHandler]`: Command handler verifying existing password, updating hash, and revoking other active sessions.

## Invariants
- `[ChangePasswordCommand.cs:ChangePasswordCommandValidator]`: New password must be normalized using FormC and contain between 8 and 128 characters.
- `[ChangePasswordCommand.cs:ChangePasswordCommandHandler]`: User must exist, be eligible to authenticate (`CanAuthenticate`), and provide valid current password.
- `[ChangePasswordCommand.cs:ChangePasswordCommandHandler]`: All active sessions for the user except `CurrentSessionId` must be revoked with reason `PasswordChanged`.

## Side Effects
- `[ChangePasswordCommand.cs:ChangePasswordCommandHandler]`: Mutates user password hash and updated timestamp in database.
- `[ChangePasswordCommand.cs:ChangePasswordCommandHandler]`: Bulk revokes concurrent active user sessions in database and evicts them from session cache.
- `[ChangePasswordCommand.cs:ChangePasswordCommandHandler]`: Writes `password_changed` security audit event.

## Verification
- Run: `dotnet test`
