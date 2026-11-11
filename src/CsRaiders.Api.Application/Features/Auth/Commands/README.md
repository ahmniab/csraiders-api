# Commands

## Overview
Aggregates authentication command packages implementing credentials verification, session token issuance/rotation, user registration, OTP verification, and password recovery.

## Subdirectories & Boundaries
- ChangePassword: [./ChangePassword/README.md](./ChangePassword/README.md) - Handles user password changes, credential checks, and concurrent session revocation.
- ForgotPassword: [./ForgotPassword/README.md](./ForgotPassword/README.md) - Generates password reset tokens and executes password resets.
- Login: [./Login/README.md](./Login/README.md) - Primary authentication command verifying credentials, managing locks, and issuing JWT/session tokens.
- Refresh: [./Refresh/README.md](./Refresh/README.md) - Refresh token rotation, device fingerprint binding, and token reuse detection.
- Register: [./Register/README.md](./Register/README.md) - Staging user account registration and queueing initial OTP emails.
- ResendOtp: [./ResendOtp/README.md](./ResendOtp/README.md) - Regeneration and resending of registration verification codes.
- VerifyOtp: [./VerifyOtp/README.md](./VerifyOtp/README.md) - Validation of 6-digit OTP codes and activation of verified user accounts.
