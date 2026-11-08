# Entities

## Overview
Core domain entities managing identities, authentication state, credential verification, sessions, role-based access control, and brute-force throttling.

## Interface
- [LoginAttempt.cs:LoginGate]: Readonly value struct representing login gate status (`Open`, `Throttled`, `Locked`) and calculating `RetryAfter`.
- [LoginAttempt.cs:LoginAttempt]: Durable brute-force tracking per email (`Evaluate`, `RegisterFailure`, `Reset`).
- [PasswordResetToken.cs:PasswordResetToken]: Single-use password reset token lifecycle entity (`Issue`, `IsUsable`, `TryConsume`, `Invalidate`).
- [PendingRegistration.cs:PendingRegistration]: Registration pre-verification record guarding OTP verification and rate limiting (`Start`, `Reissue`, `Verify`).
- [Role.cs:PermissionChange]: Record tracking added and removed permissions and whether `session:multi` is affected (`TouchesSessionMulti`).
- [Role.cs:Role]: Role definition with permission set management and scope enforcement (`Create`, `SetPermissions`).
- [Session.cs:Session]: Active login session tracking refresh token rotation, device fingerprint, idle/absolute expiry, and revocation (`Open`, `IsActive`, `TryRotate`, `TouchIfDue`, `Revoke`).
- [User.cs:User]: Aggregate root representing a system user with authentication lifecycle, password updates, suspension, role assignments, and permission overrides (`RegisterVerified`, `ChangePassword`, `UpgradePasswordHash`, `Suspend`, `Reactivate`, `SoftDelete`, `AssignRole`, `RemoveRole`, `SetPermissionOverride`, `ClearPermissionOverride`).
- [UserRole.cs:UserRole]: Join entity representing user-role assignments.
- [UserRole.cs:UserPermission]: Join entity representing explicit user permission overrides (`Update`).

## Invariants
- [LoginAttempt.cs:LoginAttempt.Evaluate]: Locked and throttled states must be evaluated before verifying passwords to prevent unnecessary Argon2id computations.
- [LoginAttempt.cs:LoginAttempt.RegisterFailure]: Lockout triggers at `AuthRules.LoginLockoutAfterFailures` (10), while exponential backoff triggers above `AuthRules.LoginDelayAfterFailures` (3).
- [PasswordResetToken.cs:PasswordResetToken.TryConsume]: Can be consumed at most once (`UsedAt` must be null and `ExpiresAt` not elapsed).
- [PendingRegistration.cs:PendingRegistration.PasswordHash]: Never stores plaintext passwords; guarded by `PasswordHashGuard.EnsureHashed`.
- [PendingRegistration.cs:PendingRegistration.Verify]: Uses constant-time comparison via `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.
- [Role.cs:Role.SetPermissions]: Enforces known permissions and restricts `RoleScope.Student` roles from holding non-student permissions.
- [Session.cs:Session.TryRotate]: Refresh token rotation detects token reuse via `PreviousRefreshTokenHash` using constant-time comparison and revokes the session on reuse detection; verifies device fingerprint match.
- [Session.cs:Session.Revoke]: Idempotent revocation; the first recorded revocation reason wins.
- [User.cs:User.CanAuthenticate]: True only when `Status == UserStatus.Active` and `DeletedAt` is null.
- [User.cs:User.SetPermissionOverride]: Permission override must be a valid known permission in `PermissionCatalog`.

## Side Effects
- [LoginAttempt.cs:LoginAttempt.RegisterFailure]: Mutates failed attempt counters, failure timestamps, and lockout/delay deadlines.
- [PasswordResetToken.cs:PasswordResetToken.TryConsume]: Sets `UsedAt` to mark token consumed.
- [PendingRegistration.cs:PendingRegistration.Verify]: Decrements `AttemptsLeft` on invalid candidate hash verification.
- [Session.cs:Session.TryRotate]: Mutates refresh token hashes, rotation timestamps, and idle expiration deadlines; revokes session if reuse is detected.
- [Session.cs:Session.Revoke]: Mutates `RevokedAt` and `RevokeReason`.
- [User.cs:User.SoftDelete]: Mutates `Status` to `UserStatus.Deleted` and sets `DeletedAt`.
- [User.cs:User.AssignRole]: Modifies the internal roles collection.
- [User.cs:User.SetPermissionOverride]: Adds or updates entry in internal permission overrides collection.

## Verification
- Run: `dotnet test`
