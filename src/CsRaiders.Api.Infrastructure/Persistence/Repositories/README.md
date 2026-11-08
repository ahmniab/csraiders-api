# Repositories

## Overview
Implements application repository interfaces using Entity Framework Core, providing data access, querying, cursor-based pagination, entity tracking, and bulk SQL update and delete operations for domain aggregates.

## Interface
- `[LoginAttemptRepository.cs:LoginAttemptRepository]` - Implements `ILoginAttemptRepository` for querying, staging, resetting, and cleaning up login attempt records.
- `[PasswordResetTokenRepository.cs:PasswordResetTokenRepository]` - Implements `IPasswordResetTokenRepository` for managing password reset tokens and batch invalidations.
- `[PendingRegistrationRepository.cs:PendingRegistrationRepository]` - Implements `IPendingRegistrationRepository` for managing staged user registrations and expiration deletions.
- `[RoleRepository.cs:RoleRepository]` - Implements `IRoleRepository` for querying roles by identifier or slug, listing roles, and persisting role entities.
- `[SessionRepository.cs:SessionRepository]` - Implements `ISessionRepository` for session lookups by hash, active session counts, single-session enforcement, and batch revocations.
- `[UserRepository.cs:UserRepository]` - Implements `IUserRepository` for fetching users, eagerly loading permissions, verifying email existence, cursor-based pagination, and user persistence.

## Invariants
- `[RoleRepository.cs:RoleRepository.FindBySlugAsync]` - Normalizes slug queries to lowercase trimmed strings before querying.
- `[UserRepository.cs:UserRepository.FindByEmailAsync]` - Suppresses soft-deleted records unless `includeDeleted: true` is explicitly provided.
- `[UserRepository.cs:UserRepository.ListAsync]` - Enforces pagination limit within [1, 100] via `Math.Clamp`.
- `[SessionRepository.cs:SessionRepository.RevokeOldestNAsync]` - Returns immediately with 0 if revocation count is less than or equal to zero.

## Side Effects
- `[LoginAttemptRepository.cs:LoginAttemptRepository.AddAsync]` - Stages `LoginAttempt` entity in DbContext change tracker.
- `[LoginAttemptRepository.cs:LoginAttemptRepository.ResetByUserIdAsync]` - Executes relational SQL `UPDATE` via `ExecuteUpdateAsync` resetting attempt counters.
- `[LoginAttemptRepository.cs:LoginAttemptRepository.DeleteStaleAsync]` - Executes relational SQL `DELETE` via `ExecuteDeleteAsync` removing stale attempts.
- `[PasswordResetTokenRepository.cs:PasswordResetTokenRepository.AddAsync]` - Stages `PasswordResetToken` in DbContext change tracker.
- `[PasswordResetTokenRepository.cs:PasswordResetTokenRepository.InvalidateAllByUserIdAsync]` - Executes relational SQL `UPDATE` via `ExecuteUpdateAsync` marking tokens used.
- `[PendingRegistrationRepository.cs:PendingRegistrationRepository.AddAsync]` - Stages `PendingRegistration` entity in DbContext change tracker.
- `[PendingRegistrationRepository.cs:PendingRegistrationRepository.DeleteByEmailAsync]` - Executes relational SQL `DELETE` via `ExecuteDeleteAsync` removing registration by email.
- `[PendingRegistrationRepository.cs:PendingRegistrationRepository.DeleteExpiredAsync]` - Executes relational SQL `DELETE` via `ExecuteDeleteAsync` pruning expired registrations.
- `[RoleRepository.cs:RoleRepository.AddAsync]` - Stages `Role` entity in DbContext change tracker.
- `[SessionRepository.cs:SessionRepository.AddAsync]` - Stages `Session` entity in DbContext change tracker.
- `[SessionRepository.cs:SessionRepository.BulkRevokeByUserIdAsync]` - Executes relational SQL `UPDATE` via `ExecuteUpdateAsync` revoking user sessions.
- `[SessionRepository.cs:SessionRepository.RevokeOldestNAsync]` - Queries oldest sessions and executes relational SQL `UPDATE` via `ExecuteUpdateAsync`.
- `[SessionRepository.cs:SessionRepository.RevokeAllByUserIdAsync]` - Executes relational SQL `UPDATE` via `ExecuteUpdateAsync` revoking all user sessions.
- `[SessionRepository.cs:SessionRepository.SetEnforceSingleSessionAsync]` - Executes relational SQL `UPDATE` via `ExecuteUpdateAsync` toggling single-session flag.
- `[UserRepository.cs:UserRepository.AddAsync]` - Stages `User` entity in DbContext change tracker.

## Verification
- Run: `dotnet test`
