# Login

## Overview
Implements primary authentication command processing, including credential verification, login attempt rate-limiting/lockouts, password re-hashing, concurrency locking, session policy enforcement, effective permission bitmask resolution and caching, and JWT access/refresh token generation.

## Interface
- `[LoginCommand.cs:LoginCommand]`: Mediator command containing `Email`, `Password`, client `Ip`, `DeviceFingerprint`, and `UserAgent`.
- `[LoginCommand.cs:LoginResult]`: Result record containing tokens (`AccessToken`, `RefreshToken`), `SessionId`, `DeviceFingerprint`, `ErrorCode`, and `RetryAfter`.
- `[LoginCommand.cs:LoginCommandValidator]`: FluentValidation validator verifying required email, password, and device fingerprint.
- `[LoginCommand.cs:LoginCommandHandler]`: Command handler orchestrating lockout checks, password verification, session limit decisions, permission bitmask resolution, and token issuance.

## Invariants
- `[LoginCommand.cs:LoginCommandHandler]`: Must verify that login gate status is `Open` before checking user credentials.
- `[LoginCommand.cs:LoginCommandHandler]`: Must acquire an advisory lock on user ID (`AcquireLoginLockAsync`) to prevent race conditions during session creation.
- `[LoginCommand.cs:LoginCommandHandler]`: Enforces active session limits according to user privileges (`SessionMulti`) and system cap via `SessionPolicy.Decide`.
- `[LoginCommand.cs:LoginCommandHandler]`: Retries session creation once upon unique constraint violation (`uq_sessions_one_active`).
- `[LoginCommand.cs:LoginCommandHandler]`: Resolves effective permissions, maps them to a 64-bit scalar bitmask via `PermissionBitmaskMapper`, memoizes the mask under `perms_mask:{userId}` linked to `IRbacInvalidationToken`, and supplies it to `CreateAccessToken`.

## Side Effects
- `[LoginCommand.cs:LoginCommandHandler]`: Records login failures in `ILoginAttemptTracker` and writes `login_locked_out` audit log when lockout threshold is reached.
- `[LoginCommand.cs:LoginCommandHandler]`: Resets failed login attempts upon successful authentication.
- `[LoginCommand.cs:LoginCommandHandler]`: Upgrades stored password hash when `NeedsRehash` is true.
- `[LoginCommand.cs:LoginCommandHandler]`: Revokes superseded or cap-exceeded active sessions, evicting them from session cache.
- `[LoginCommand.cs:LoginCommandHandler]`: Inserts new session record and commits changes via `IApplicationDbContext.SaveChangesAsync`.
- `[LoginCommand.cs:LoginCommandHandler]`: Stores the resolved permissions bitmask in `IMemoryCache` under `perms_mask:{userId}` with sliding expiration linked to `IRbacInvalidationToken.CurrentToken`.

## Verification
- Run: `dotnet test`
