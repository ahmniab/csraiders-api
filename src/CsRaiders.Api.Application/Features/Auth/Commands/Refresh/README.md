# Refresh

## Overview
Processes refresh token rotation requests, validating cryptographic token signatures, binding device fingerprints, detecting refresh token reuse attacks, and issuing new access and refresh tokens.

## Interface
- `[RefreshTokenCommand.cs:RefreshTokenCommand]`: Mediator command with `RefreshToken` and `DeviceFingerprint`.
- `[RefreshTokenCommand.cs:RefreshTokenResult]`: Result record containing new `AccessToken`, rotated `RefreshToken`, `SessionId`, and `ErrorCode`.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Command handler performing token validation, pessimistic session locking, rotation, and reuse handling.

## Invariants
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Validates refresh token claims and expiration against `clock.UtcNow` before checking session state.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Must acquire pessimistic update lock on session (`FindByIdForUpdateAsync`) to prevent race conditions during rotation.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: If token reuse is detected (`RefreshOutcome.ReuseDetected`), session is marked compromised and rejected.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Rejects rotation if device fingerprint does not match the active session.

## Side Effects
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Updates session with rotated refresh token hash and last active timestamp in database.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Evicts previous session state from session cache.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Logs `token_reuse_detected` security audit event when token replay is detected.

## Verification
- Run: `dotnet test`
