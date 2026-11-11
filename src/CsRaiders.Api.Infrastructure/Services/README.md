# Services

## Overview
Provides supporting infrastructure runtime services, background hosted workers for auth cleanup and asynchronous email dispatch, rate-limiting and login attempt tracking, metrics collection, security audit writing, session reconciliation, and caching hooks.

## Interface
- `[AppSettingsProvider.cs:AppSettingsProvider]` - Implements `IAppSettingsProvider` to cache and resolve multi-session concurrency cap (`session.multi_max_concurrent`) with a 5-second TTL.
- `[AuthCleanupWorker.cs:AuthCleanupWorker]` - Hosted `BackgroundService` executing periodic cleanup of stale sessions, expired registrations, stale password reset tokens, stale login attempts, and aged security events.
- `[AuthCleanupWorker.cs:CleanupCounts]` - Record capturing deleted count tallies across cleaned aggregates.
- `[AuthMetricsRecorder.cs:AuthMetricsRecorder]` - Implements `IAuthMetricsRecorder` using `ConcurrentDictionary` for thread-safe authentication metrics recording and snapshotting.
- `[ChannelEmailQueue.cs:ChannelEmailQueue]` - Implements `IEmailQueue` backed by a bounded `System.Threading.Channels.Channel<EmailMessage>` (capacity 1000, drop on full).
- `[EmailSenderWorker.cs:EmailSenderWorker]` - Hosted `BackgroundService` draining `IEmailQueue` and sending messages via `IEmailSender` with exponential retry policy.
- `[EmailSenderWorker.cs:IEmailSender]` - Abstraction for email transmission dispatchers.
- `[EmailSenderWorker.cs:ConsoleEmailSender]` - Implements `IEmailSender` logging queued email transmissions with redacted recipient details.
- `[LoginAttemptTracker.cs:LoginAttemptTracker]` - Implements `ILoginAttemptTracker` for evaluating login gates (with distributed attack thresholds), registering failures, and resetting attempt counters.
- `[RbacInvalidationToken.cs:RbacInvalidationToken]` - Implements `IRbacInvalidationToken` using a cancellation token source to signal cache invalidation across the application on RBAC policy mutations.
- `[SecurityAuditWriter.cs:SecurityAuditWriter]` - Implements `ISecurityAuditWriter` asynchronously persisting redacted security events using an isolated `IDbContextFactory<AppDbContext>`.
- `[SessionCacheHook.cs:MemorySessionCacheHook]` - Implements `ISessionCacheHook` tracking in-memory session cache evictions.
- `[SessionReconciliationService.cs:SessionReconciliationService]` - Implements `ISessionReconciliationService` serializing session reconciliation via PostgreSQL advisory locks when multi-session capability is lost.
- `[SystemPlatformClock.cs:SystemPlatformClock]` - Implements `IPlatformClock` returning the system `DateTimeOffset.UtcNow`.

## Invariants
- `[AppSettingsProvider.cs:AppSettingsProvider.GetMultiSessionCapAsync]` - Caches resolved session cap for 5 seconds; falls back to `AuthRules.DefaultMultiSessionCap` if the setting is absent or invalid.
- `[AuthCleanupWorker.cs:AuthCleanupWorker.RunCleanupAsync]` - Ensures only one cleanup pass executes at a time via `SemaphoreSlim(1, 1)` with non-blocking try-wait.
- `[ChannelEmailQueue.cs:ChannelEmailQueue]` - Bounded to 1000 items with `BoundedChannelFullMode.DropWrite` to prevent unbounded memory growth during spikes.
- `[EmailSenderWorker.cs:EmailSenderWorker.ExecuteAsync]` - Retries failed sends up to 3 times (delays 0s, 1s, 5s) with a 10-second cancellation timeout per send attempt.
- `[LoginAttemptTracker.cs:LoginAttemptTracker.EvaluateAsync]` - Throttles logins if email-wide recent failure count reaches or exceeds 30 within `AuthRules.LoginFailureWindow` (mitigating distributed brute force attacks).
- `[SecurityAuditWriter.cs:SecurityAuditWriter.Redact]` - Recursively redacts sensitive payload properties (`password_hash`, `otp_hash`, `refresh_token_hash`, `previous_refresh_token_hash`, `token_hash`, `password`, `otp`, `refresh_token`).
- `[SessionReconciliationService.cs:SessionReconciliationService.ReconcileAfterMultiSessionLossAsync]` - Acquires database transactional advisory lock before revoking non-survivor sessions and enabling `enforce_single_session` to prevent partial unique index violations.

## Side Effects
- `[AuthCleanupWorker.cs:AuthCleanupWorker.RunCleanupAsync]` - Deletes stale sessions (>30 days old), expired pending registrations, stale password reset tokens (>7 days old), stale login attempts (>24 hours old), and old security events (>30 days old) from the database.
- `[ChannelEmailQueue.cs:ChannelEmailQueue.TryEnqueue]` - Writes `EmailMessage` to channel and increments metrics.
- `[EmailSenderWorker.cs:EmailSenderWorker.ExecuteAsync]` - Dispatches messages from queue, logs warnings on retry, and increments metric counters.
- `[LoginAttemptTracker.cs:LoginAttemptTracker.RegisterFailureAsync]` - Adds or updates `LoginAttempt` entity via repository.
- `[LoginAttemptTracker.cs:LoginAttemptTracker.ResetAsync]` - Updates `LoginAttempt` entity resetting failure counts.
- `[RbacInvalidationToken.cs:RbacInvalidationToken.Invalidate]` - Cancels and recreates internal `CancellationTokenSource`.
- `[SecurityAuditWriter.cs:SecurityAuditWriter.WriteAsync]` - Increments metric counter, creates a new `AppDbContext` instance, stages `SecurityEvent`, and persists it to the PostgreSQL database.
- `[SessionCacheHook.cs:MemorySessionCacheHook.Evict]` - Mutates in-memory eviction set.
- `[SessionReconciliationService.cs:SessionReconciliationService.ReconcileAfterMultiSessionLossAsync]` - Acquires PostgreSQL advisory transaction lock, revokes sessions, enqueues post-commit cache evictions, and updates single-session enforcement in the database.

## Verification
- Run: `dotnet test`
