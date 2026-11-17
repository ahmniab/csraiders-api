# Entities

## Overview
Core domain entities managing identities, authentication state, credential verification, sessions, role-based access control, brute-force throttling, security auditing, and application settings.

> **Note (anemic model):** entities here are pure property bags. All behavior has moved to the Application layer:
> creation logic lives in `CsRaiders.Api.Application/Factories/*Factory.cs` (e.g. `UserFactory.RegisterVerified`, `SessionFactory.Open`),
> and mutation/state logic lives in `CsRaiders.Api.Application/Extensions/*Extensions.cs` (e.g. `SessionExtensions.TryRotate`, `UserExtensions.Suspend`).
> The invariants documented below still hold — they are now enforced by the extensions, not the entities.
> Value objects (`Email`, `LoginGate`, `PermissionChange`) and domain constants/policies remain in Domain.

## Interface
- [AppSetting.cs:AppSetting]: Key-value configuration entity tracking mutable runtime settings (`Create`, `SetValue`).
- [EmailMessage.cs:EmailPurpose]: Enumeration defining the contextual purpose of an email dispatch (`RegistrationOtp`, `PasswordReset`, `AccountNotice`).
- [EmailMessage.cs:EmailMessage]: Immutable domain record representing an outbound email dispatch (`To`, `Subject`, `Body`, `Purpose`, `CorrelationId`).
- [LoginAttempt.cs:LoginGate]: Readonly value struct representing login gate status (`Open`, `Throttled`, `Locked`) and calculating `RetryAfter`.
- [LoginAttempt.cs:LoginAttempt]: Durable brute-force tracking per email (`Evaluate`, `RegisterFailure`, `Reset`).
- [PasswordResetToken.cs:PasswordResetToken]: Single-use password reset token lifecycle entity (`Issue`, `IsUsable`, `TryConsume`, `Invalidate`).
- [PendingRegistration.cs:PendingRegistration]: Registration pre-verification record guarding OTP verification and rate limiting (`Start`, `Reissue`, `Verify`).
- [Role.cs:PermissionChange]: Record tracking added and removed permissions and whether `session:multi` is affected (`TouchesSessionMulti`).
- [Role.cs:Role]: Role definition with permission set management, scope enforcement, and detail updating (`Create`, `SetPermissions`, `UpdateDetails`).
- [SecurityEvent.cs:SecurityEvent]: Immutable domain security event audit entry recording security-sensitive operations (`Create`).
- [Session.cs:Session]: Active login session tracking refresh token rotation, device fingerprint, idle/absolute expiry, and revocation (`Open`, `IsActive`, `TryRotate`, `TouchIfDue`, `Revoke`).
- [User.cs:User]: Aggregate root representing a system user with authentication lifecycle, password updates, suspension, role assignments, and permission overrides (`RegisterVerified`, `ChangePassword`, `UpgradePasswordHash`, `Suspend`, `Reactivate`, `SoftDelete`, `AssignRole`, `RemoveRole`, `SetPermissionOverride`, `ClearPermissionOverride`).
- [UserRole.cs:UserRole]: Join entity representing user-role assignments.
- [UserRole.cs:UserPermission]: Join entity representing explicit user permission overrides (`Update`).

## Invariants
- [AppSetting.cs:AppSetting.Key]: Primary lookup identifier representing system configuration key; non-null.
- [LoginAttempt.cs:LoginAttempt.Evaluate]: Locked and throttled states must be evaluated before verifying passwords to prevent unnecessary Argon2id computations.
- [LoginAttempt.cs:LoginAttempt.RegisterFailure]: Lockout triggers at `AuthRules.LoginLockoutAfterFailures` (10), while exponential backoff triggers above `AuthRules.LoginDelayAfterFailures` (3).
- [PasswordResetToken.cs:PasswordResetToken.TryConsume]: Can be consumed at most once (`UsedAt` must be null and `ExpiresAt` not elapsed).
- [PendingRegistration.cs:PendingRegistration.PasswordHash]: Never stores plaintext passwords; guarded by `PasswordHashGuard.EnsureHashed`.
- [PendingRegistration.cs:PendingRegistration.Verify]: Uses constant-time comparison via `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.
- [Role.cs:Role.SetPermissions]: Enforces known permissions and restricts `RoleScope.Student` roles from holding non-student permissions.
- [Role.cs:Role.UpdateDetails]: System roles are immutable (`DomainException("system_role_immutable")`); student roles cannot hold staff permissions.
- [SecurityEvent.cs:SecurityEvent.Details]: Defaults to empty JSON document `{}` if omitted.
- [Session.cs:Session.TryRotate]: Refresh token rotation detects token reuse via `PreviousRefreshTokenHash` using constant-time comparison and revokes the session on reuse detection; verifies device fingerprint match.
- [Session.cs:Session.Revoke]: Idempotent revocation; the first recorded revocation reason wins.
- [User.cs:User.CanAuthenticate]: True only when `Status == UserStatus.Active` and `DeletedAt` is null.
- [User.cs:User.Suspend]: Throws `DomainException("user_deleted")` if user is already deleted.
- [User.cs:User.Reactivate]: Throws `DomainException("user_not_suspended")` if user is not in `Suspended` state.
- [User.cs:User.SetPermissionOverride]: Permission override must be a valid known permission in `PermissionCatalog`.

## Side Effects
- [AppSetting.cs:AppSetting.SetValue]: Mutates `Value` and `UpdatedAt`.
- [LoginAttempt.cs:LoginAttempt.RegisterFailure]: Mutates failed attempt counters, failure timestamps, and lockout/delay deadlines.
- [LoginAttempt.cs:LoginAttempt.Reset]: Clears failed attempt counters and failure deadlines.
- [PasswordResetToken.cs:PasswordResetToken.TryConsume]: Sets `UsedAt` to mark token consumed.
- [PasswordResetToken.cs:PasswordResetToken.Invalidate]: Sets `UsedAt` if not already set.
- [PendingRegistration.cs:PendingRegistration.Reissue]: Resets attempts left, advances send window counter, updates OTP hash, and recalculates expiration deadlines.
- [PendingRegistration.cs:PendingRegistration.Verify]: Decrements `AttemptsLeft` on invalid candidate hash verification.
- [Role.cs:Role.SetPermissions]: Mutates internal permissions list.
- [Role.cs:Role.UpdateDetails]: Mutates `Name`, `Slug`, and `Scope`.
- [Session.cs:Session.TryRotate]: Mutates refresh token hashes, rotation timestamps, and idle expiration deadlines; revokes session if reuse is detected.
- [Session.cs:Session.TouchIfDue]: Mutates `LastSeenAt` and `IdleExpiresAt` if throttle window has elapsed.
- [Session.cs:Session.Revoke]: Mutates `RevokedAt` and `RevokeReason`.
- [User.cs:User.ChangePassword]: Mutates `PasswordHash` and updates `PasswordChangedAt`.
- [User.cs:User.UpgradePasswordHash]: Mutates `PasswordHash` without modifying `PasswordChangedAt`.
- [User.cs:User.Suspend]: Mutates `Status` to `UserStatus.Suspended`.
- [User.cs:User.Reactivate]: Mutates `Status` to `UserStatus.Active`.
- [User.cs:User.SoftDelete]: Mutates `Status` to `UserStatus.Deleted` and sets `DeletedAt`.
- [User.cs:User.AssignRole]: Modifies the internal roles collection.
- [User.cs:User.RemoveRole]: Removes matching role from internal roles collection.
- [User.cs:User.SetPermissionOverride]: Adds or updates entry in internal permission overrides collection.
- [User.cs:User.ClearPermissionOverride]: Removes entry from internal permission overrides collection.
- [UserRole.cs:UserPermission.Update]: Mutates `Effect`, `GrantedBy`, and `GrantedAt`.

## Verification
- Run: `dotnet test`
