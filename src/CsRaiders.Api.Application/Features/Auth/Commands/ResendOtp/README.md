# ResendOtp

## Overview
Handles requests to resend registration one-time passwords (OTP), enforcing enumeration resistance, rate limits, and dispatching updated OTP emails.

## Interface
- `[ResendOtpCommand.cs:ResendOtpCommand]`: Mediator command with `Email`.
- `[ResendOtpCommand.cs:ResendOtpResult]`: Result record containing `Accepted` and `CapacityExhausted`.
- `[ResendOtpCommand.cs:ResendOtpCommandValidator]`: FluentValidation validator verifying email address format.
- `[ResendOtpCommand.cs:ResendOtpCommandHandler]`: Command handler verifying registration status, regenerating OTP, and queueing outbound email.

## Invariants
- `[ResendOtpCommand.cs:ResendOtpCommandHandler]`: Returns generic `Accepted` result regardless of whether account exists, preserving enumeration resistance.
- `[ResendOtpCommand.cs:ResendOtpCommandHandler]`: If user is already registered, enqueues notice email without generating an OTP.
- `[ResendOtpCommand.cs:ResendOtpCommandHandler]`: Enforces domain reissue rate limits and expiration on pending registrations.

## Side Effects
- `[ResendOtpCommand.cs:ResendOtpCommandHandler]`: Generates and encrypts new OTP, updating the pending registration entity.
- `[ResendOtpCommand.cs:ResendOtpCommandHandler]`: Enqueues verification or notice email into `IEmailQueue`.

## Verification
- Run: `dotnet test`
