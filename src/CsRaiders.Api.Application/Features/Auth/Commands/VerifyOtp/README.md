# VerifyOtp

## Overview
Validates 6-digit registration one-time passwords (OTP), finalizes pending user registrations into verified user accounts, assigns the default system student role, and cleans up pending staging records.

## Interface
- `[VerifyOtpCommand.cs:VerifyOtpCommand]`: Mediator command containing `Email` and 6-digit `Code`.
- `[VerifyOtpCommand.cs:VerifyOtpResult]`: Result record indicating verification success or `InvalidOrExpired` failure.
- `[VerifyOtpCommand.cs:VerifyOtpCommandValidator]`: FluentValidation validator ensuring valid email and 6-digit numeric pattern.
- `[VerifyOtpCommand.cs:VerifyOtpCommandHandler]`: Command handler verifying OTP code, creating verified user account, and purging staging records.

## Invariants
- `[VerifyOtpCommand.cs:VerifyOtpCommandValidator]`: Code must be exactly 6 numeric digits (`^[0-9]{6}$`).
- `[VerifyOtpCommand.cs:VerifyOtpCommandHandler]`: Requires an existing `PendingRegistration` whose OTP verification status resolves to `Verified`.
- `[VerifyOtpCommand.cs:VerifyOtpCommandHandler]`: Newly verified users are assigned the system `student` role by default.

## Side Effects
- `[VerifyOtpCommand.cs:VerifyOtpCommandHandler]`: Creates and persists new `User` entity with assigned role in database.
- `[VerifyOtpCommand.cs:VerifyOtpCommandHandler]`: Deletes `PendingRegistration` record for verified email.
- `[VerifyOtpCommand.cs:VerifyOtpCommandHandler]`: Writes `registration_otp_failed` security audit log upon OTP failure.

## Verification
- Run: `dotnet test`
