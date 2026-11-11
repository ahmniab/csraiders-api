# ForgotPassword

## Overview
Handles password recovery workflows, including generating secure reset tokens, dispatching recovery emails, validating token hashes, and executing password resets with session revocation.

## Interface
- `[ForgotPasswordCommand.cs:ForgotPasswordCommand]`: Mediator command with user `Email` and client `Ip`.
- `[ForgotPasswordCommand.cs:ForgotPasswordResult]`: Result record indicating acceptance or capacity exhaustion.
- `[ForgotPasswordCommand.cs:ForgotPasswordCommandValidator]`: FluentValidation validator ensuring email is present and well-formed.
- `[ForgotPasswordCommand.cs:ForgotPasswordCommandHandler]`: Command handler issuing cryptographically secure reset tokens and queueing notification emails.
- `[ForgotPasswordCommand.cs:ResetPasswordCommand]`: Mediator command with reset `Token` and new `Password`.
- `[ForgotPasswordCommand.cs:ResetPasswordResult]`: Result record conveying password reset outcome and error code.
- `[ForgotPasswordCommand.cs:ResetPasswordCommandValidator]`: FluentValidation validator enforcing token presence and password length rules.
- `[ForgotPasswordCommand.cs:ResetPasswordCommandHandler]`: Command handler verifying reset token validity, applying new password hash, and revoking all user sessions.

## Invariants
- `[ForgotPasswordCommand.cs:ForgotPasswordCommandHandler]`: Returns generic accepted response even if email is not found or rate-limited to avoid user enumeration.
- `[ForgotPasswordCommand.cs:ForgotPasswordCommandHandler]`: Enforces rate limit cap (`OtpMaxSendsPerHour`) within `OtpSendWindow`.
- `[ForgotPasswordCommand.cs:ResetPasswordCommandHandler]`: Token validation must use constant-time comparison (`FixedTimeEquals`) and verify `IsUsable` before consuming.
- `[ForgotPasswordCommand.cs:ResetPasswordCommandHandler]`: Successfully resetting password revokes all active sessions for the user.

## Side Effects
- `[ForgotPasswordCommand.cs:ForgotPasswordCommandHandler]`: Invalidates prior reset tokens for user, saves new hashed reset token, queues email in `IEmailQueue`, and writes `password_reset_issued` audit event.
- `[ForgotPasswordCommand.cs:ResetPasswordCommandHandler]`: Marks reset token consumed, updates user password hash, revokes all active user sessions in database, evicts them from session cache, resets failed login attempts, and writes `password_reset_completed` audit log.

## Verification
- Run: `dotnet test`
