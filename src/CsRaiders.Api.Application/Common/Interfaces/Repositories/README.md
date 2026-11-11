# Repositories

## Overview
Defines contract interfaces for repository data access abstraction across core domain entities, including authentication, users, roles, sessions, settings, and registration workflows.

## Interface
- `[IAppSettingRepository.cs:IAppSettingRepository]`: Repository abstraction for reading and writing application-level settings and configuration keys.
- `[ILoginAttemptRepository.cs:ILoginAttemptRepository]`: Repository abstraction for finding, adding, resetting, and pruning login attempt records.
- `[IPasswordResetTokenRepository.cs:IPasswordResetTokenRepository]`: Repository abstraction for managing password reset tokens, lookups by token hash, and user-level invalidation.
- `[IPendingRegistrationRepository.cs:IPendingRegistrationRepository]`: Repository abstraction for managing temporary user registrations and pruning expired entries.
- `[IRoleRepository.cs:IRoleRepository]`: Repository abstraction for role retrieval (by ID, slug, or batch) and persistence.
- `[ISecurityEventRepository.cs:ISecurityEventRepository]`: Repository abstraction for recording security audit events and purging historical logs.
- `[ISessionRepository.cs:ISessionRepository]`: Repository abstraction for querying active sessions, listing summaries, handling revocation, and enforcing single session constraints.
- `[IUserRepository.cs:IUserRepository]`: Repository abstraction for user lookups, existence checks, permission-resolved entity loading, advisory login locking, and keyset-paginated user listing.

## Invariants
- `[IUserRepository.cs:FindByIdWithPermissionsAsync]`: Must load User along with associated Roles and PermissionOverrides in a split query for permission resolution.
- `[IUserRepository.cs:ListAsync]`: Must perform keyset pagination using `UserFilter.CursorId` and `UserFilter.Limit`.
- `[ISessionRepository.cs:BulkRevokeByUserIdAsync]`: Revokes user sessions while preserving `exceptSessionId` if specified.

## Side Effects
- `[IAppSettingRepository.cs:SetAsync]`: Updates or inserts application configuration setting in persistence store.
- `[ILoginAttemptRepository.cs:AddAsync]`: Queues new login attempt record for persistence.
- `[ILoginAttemptRepository.cs:Update]`: Marks login attempt record as modified.
- `[ILoginAttemptRepository.cs:ResetByUserIdAsync]`: Resets login attempts in persistence storage for a specific user.
- `[ILoginAttemptRepository.cs:DeleteStaleAsync]`: Deletes stale login attempts from storage.
- `[IPasswordResetTokenRepository.cs:AddAsync]`: Queues new password reset token for persistence.
- `[IPasswordResetTokenRepository.cs:Update]`: Marks password reset token as modified.
- `[IPasswordResetTokenRepository.cs:InvalidateAllByUserIdAsync]`: Invalidates active password reset tokens for a user.
- `[IPendingRegistrationRepository.cs:AddAsync]`: Queues pending registration record for persistence.
- `[IPendingRegistrationRepository.cs:Update]`: Marks pending registration as modified.
- `[IPendingRegistrationRepository.cs:DeleteByEmailAsync]`: Deletes pending registration matching email.
- `[IPendingRegistrationRepository.cs:DeleteExpiredAsync]`: Deletes expired pending registrations from storage.
- `[IRoleRepository.cs:AddAsync]`: Queues new role for persistence.
- `[IRoleRepository.cs:Update]`: Marks role entity as modified.
- `[ISecurityEventRepository.cs:AddAsync]`: Appends security event audit log entry to persistence store.
- `[ISecurityEventRepository.cs:PurgeBeforeAsync]`: Purges security audit event records older than cutoff timestamp from storage.
- `[ISessionRepository.cs:AddAsync]`: Queues new session for persistence.
- `[ISessionRepository.cs:Update]`: Marks session as modified.
- `[ISessionRepository.cs:BulkRevokeByUserIdAsync]`: Updates revocation state of matching user sessions in storage.
- `[ISessionRepository.cs:RevokeOldestNAsync]`: Revokes the N oldest active sessions for a user.
- `[ISessionRepository.cs:RevokeAllByUserIdAsync]`: Revokes all active sessions for a user.
- `[ISessionRepository.cs:SetEnforceSingleSessionAsync]`: Updates single session enforcement status and revokes redundant sessions.
- `[IUserRepository.cs:AddAsync]`: Queues new user entity for persistence.
- `[IUserRepository.cs:Update]`: Marks user entity as modified.
- `[IUserRepository.cs:AcquireLoginLockAsync]`: Acquires transaction/advisory lock for serializing login operations on target user.

## Verification
- Run: `dotnet test`
