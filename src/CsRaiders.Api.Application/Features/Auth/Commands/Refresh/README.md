# Refresh

## Overview
Processes refresh token rotation requests, validating cryptographic token signatures, binding device fingerprints, detecting refresh token reuse attacks, retrieving memoized or resolving user permission bitmasks, and issuing new 5-minute access and rotated refresh tokens.

## Interface
- `[RefreshTokenCommand.cs:RefreshTokenCommand]`: Mediator command with `RefreshToken` and `DeviceFingerprint`.
- `[RefreshTokenCommand.cs:RefreshTokenResult]`: Result record containing new `AccessToken`, rotated `RefreshToken`, `SessionId`, and `ErrorCode`.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Command handler performing token validation, pessimistic session locking, rotation, reuse handling, and permission bitmask resolution.

## Invariants
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Validates refresh token claims and expiration against `clock.UtcNow` fail-fast before acquiring locks or querying user permissions.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Must acquire pessimistic update lock on session (`FindByIdForUpdateAsync`) to prevent race conditions during rotation.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: If token reuse is detected (`RefreshOutcome.ReuseDetected`), session is marked compromised and rejected immediately with zero permission queries.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Rejects rotation if device fingerprint does not match the active session.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Queries `IMemoryCache` for cached bitmask `perms_mask:{userId}` before executing database permission queries, protecting PostgreSQL from refresh storms.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Issues access tokens with 5-minute lifetimes containing the invariant decimal string `perms` claim.

## Side Effects
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Updates session with rotated refresh token hash and last active timestamp in database.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Evicts previous session state from session cache.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Logs `token_reuse_detected` security audit event when token replay is detected.
- `[RefreshTokenCommand.cs:RefreshTokenCommandHandler]`: Memoizes the resolved permission bitmask in `IMemoryCache` under `perms_mask:{userId}` on cache misses.

## Verification
- Run: `dotnet test`
