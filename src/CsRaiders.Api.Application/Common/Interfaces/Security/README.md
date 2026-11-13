# Security

## Overview
Defines core security abstractions including password hashing, token generation/validation, OTP generation, RBAC invalidation signals, audit logging, session reconciliation, and login throttling.

## Interface
- `[IAppSettingsProvider.cs:IAppSettingsProvider]`: Provider for dynamic security settings such as multi-session concurrency limits and cache eviction.
- `[IAuthMetricsRecorder.cs:IAuthMetricsRecorder]`: In-memory metrics recorder for authentication telemetry counters and snapshots.
- `[IEmailQueue.cs:IEmailQueue]`: Internal queuing contract for asynchronous dispatch of authentication and notification emails.
- `[ILoginAttemptTracker.cs:ILoginAttemptTracker]`: Login rate limiting and lockout gate evaluator and attempt tracker.
- `[IOtpGenerator.cs:IOtpGenerator]`: Cryptographic one-time password (OTP) generation, protection, and verification service.
- `[IPasswordHasher.cs:IPasswordHasher]`: Asynchronous password hashing and verification contract returning `PasswordHashResult` and `PasswordVerifyResult`.
- `[IPasswordHasher.cs:PasswordHashResult]`: Result struct conveying hash outcome (`Success` or `Unavailable`) and hash string.
- `[IPasswordHasher.cs:PasswordVerifyResult]`: Result struct indicating password validity, rehash recommendation, and operational outcome.
- `[IRbacInvalidationToken.cs:IRbacInvalidationToken]`: Process-wide invalidation token signaling RBAC cache eviction upon role or permission mutations.
- `[ISecurityAuditWriter.cs:ISecurityAuditWriter]`: Audit trail logging interface for recording critical security events and actor metadata.
- `[ISessionCacheHook.cs:ISessionCacheHook]`: Cache eviction notification hook for invalidated or revoked user sessions.
- `[ISessionReconciliationService.cs:ISessionReconciliationService]`: Service contract to reconcile and downgrade active sessions upon user loss of multi-session privileges.
- `[ISessionStatusReader.cs:ISessionStatusReader]`: Lightweight read-only accessor for active session expiration and revocation status.
- `[ISessionStatusReader.cs:SessionStatus]`: Record struct representing session status details (`SessionId`, `UserId`, `ExpiresAt`, `IdleExpiresAt`, `Revoked`).
- `[ITokenService.cs:ITokenService]`: JWT token issuance with 64-bit permission bitmask claims and 5-minute lifetimes, refresh token validation, and SHA256 refresh token hashing abstraction.
- `[ITokenService.cs:RefreshTokenClaims]`: Record representing parsed claims from a validated refresh token (`UserId`, `SessionId`, `TokenId`).

## Invariants
- `[IRbacInvalidationToken.cs:IRbacInvalidationToken.Invalidate]`: Must cancel the active `CurrentToken` source to trigger immediate invalidation across all cached authorization boundaries.
- `[IPasswordHasher.cs:PasswordHashResult]`: Result must have status `Success` with non-null hash, or `Unavailable` with null hash when hashing service is overloaded.
- `[ITokenService.cs:ITokenService.CreateAccessToken]`: Serializes the 64-bit integer permissions bitmask as an invariant base-10 decimal string in the `perms` claim with a 5-minute expiration window.
- `[ITokenService.cs:ITokenService.TryValidateRefreshToken]`: Must validate signature and expiration against supplied `now` timestamp before outputting `RefreshTokenClaims`.
- `[IOtpGenerator.cs:IOtpGenerator.Verify]`: Must verify that the provided OTP string matches the protected digest for the specified email address.

## Side Effects
- `[IAppSettingsProvider.cs:IAppSettingsProvider.EvictMultiSessionCap]`: Clears cached multi-session limit setting.
- `[IAuthMetricsRecorder.cs:IAuthMetricsRecorder.Increment]`: Mutates in-memory metrics counter.
- `[IEmailQueue.cs:IEmailQueue.TryEnqueue]`: Pushes an email message into the background delivery channel.
- `[ILoginAttemptTracker.cs:ILoginAttemptTracker.RegisterFailureAsync]`: Records a login failure and mutates lockout state in persistence or memory.
- `[ILoginAttemptTracker.cs:ILoginAttemptTracker.ResetAsync]`: Clears login attempt failures and unlocks the gate for the email and IP.
- `[IRbacInvalidationToken.cs:IRbacInvalidationToken.Invalidate]`: Triggers cancellation on the active CancellationTokenSource.
- `[ISecurityAuditWriter.cs:ISecurityAuditWriter.WriteAsync]`: Persists or queues security audit log events.
- `[ISessionCacheHook.cs:ISessionCacheHook.Evict]`: Evicts session entry from the distributed or local cache.
- `[ISessionReconciliationService.cs:ISessionReconciliationService.ReconcileAfterMultiSessionLossAsync]`: Revokes redundant active sessions in the database for the user.

## Verification
- Run: `dotnet test`
