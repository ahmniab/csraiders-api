# Register

## Overview
Manages initial user account registration, checking existing account uniqueness, staging unverified users in pending registrations, generating one-time verification passwords (OTP), and queueing verification emails.

## Interface
- `[RegisterCommand.cs:RegisterCommand]`: Mediator command containing `Email`, `Password`, and optional `Name`.
- `[RegisterCommand.cs:RegisterResult]`: Result record conveying `Accepted`, `CapacityExhausted`, and `AlreadyRegistered`.
- `[RegisterCommand.cs:RegisterCommandValidator]`: FluentValidation validator enforcing email format and password length boundaries (8-128 characters).
- `[RegisterCommand.cs:RegisterCommandHandler]`: Command handler managing pending registration staging and OTP email dispatch.

## Invariants
- `[RegisterCommand.cs:RegisterCommandValidator]`: Password must be normalized to Unicode FormC and length between 8 and 128 characters.
- `[RegisterCommand.cs:RegisterCommandHandler]`: Rejects registration with `AlreadyRegistered` flag if user email is already registered in active accounts.
- `[RegisterCommand.cs:RegisterCommandHandler]`: If a pending registration exists for the email, reissues OTP rather than creating duplicate records.

## Side Effects
- `[RegisterCommand.cs:RegisterCommandHandler]`: Asynchronously hashes password via `IPasswordHasher`.
- `[RegisterCommand.cs:RegisterCommandHandler]`: Inserts or updates `PendingRegistration` entity in database.
- `[RegisterCommand.cs:RegisterCommandHandler]`: Enqueues verification email with OTP to `IEmailQueue`.

## Verification
- Run: `dotnet test`
