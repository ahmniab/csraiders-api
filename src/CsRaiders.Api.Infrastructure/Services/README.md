# Services

## Overview
Provides supporting infrastructure runtime services, background hosted workers for auth cleanup and asynchronous email dispatch, rate-limiting and login attempt tracking, metrics collection, security audit writing, session reconciliation, caching hooks, real SMTP delivery with MailKit, extensible email templating, and dead-letter storage.

## Interface
- `[AppSettingsProvider.cs:AppSettingsProvider]` - Implements `IAppSettingsProvider` to cache and resolve multi-session concurrency cap (`session.multi_max_concurrent`) with a 5-second TTL.
- `[AuthCleanupWorker.cs:AuthCleanupWorker]` - Hosted `BackgroundService` executing periodic cleanup of stale sessions, expired registrations, stale password reset tokens, stale login attempts, and aged security events.
- `[AuthCleanupWorker.cs:CleanupCounts]` - Record capturing deleted count tallies across cleaned aggregates.
- `[AuthMetricsRecorder.cs:AuthMetricsRecorder]` - Implements `IAuthMetricsRecorder` using `ConcurrentDictionary` for thread-safe authentication metrics recording and snapshotting.
- `[ChannelEmailQueue.cs:ChannelEmailQueue]` - Implements `IEmailQueue` backed by a bounded `System.Threading.Channels.Channel<EmailMessage>` (capacity 1000, drop on full).
- `[EmailSenderWorker.cs:EmailSenderWorker]` - Hosted `BackgroundService` draining `IEmailQueue` and sending messages via `IEmailSender` with exponential retry policy (0s, 1s, 5s), log sanitization, failure metrics, dead-letter persistence via `IDeadLetterEmailRepository`, and in-process failure events via `IPublisher`.
- `[EmailSenderWorker.cs:IEmailSender]` - Abstraction for email transmission dispatchers.
- `[EmailSenderWorker.cs:ConsoleEmailSender]` - Implements `IEmailSender` logging queued email transmissions with redacted recipient details for development environments.
- `[SmtpEmailSender.cs:SmtpEmailSender]` - Implements `IEmailSender` using MailKit `SmtpClient` over secure TLS/STARTTLS with configurable timeout, authentication, and multipart/alternative MIME message construction.
- `[EmailTemplateService.cs:EmailTemplateService]` - Implements `IEmailTemplateService` validating dynamic parameters, normalizing recipient addresses, and rendering HTML + plain-text email templates into `EmailMessage` payloads.
- `[TemplatedEmailQueue.cs:TemplatedEmailQueue]` - Implements `ITemplatedEmailQueue` providing a unified facade for atomic template rendering and non-blocking delivery channel queuing.
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
- `[EmailSenderWorker.cs:EmailSenderWorker.ExecuteAsync]` - Retries failed sends up to 3 times (delays 0s, 1s, 5s) with a 10-second cancellation timeout per send attempt; on retry exhaustion, persists to `DeadLetterEmail` in database and emits `EmailDeliveryFailedNotification` without crashing the hosted service.
- `[SmtpEmailSender.cs:SmtpEmailSender.SendAsync]` - Connects securely using configured SSL/TLS mode; redacts recipient addresses in logs (`j***e@domain.com`); omits OTPs and sensitive payloads from logs.
- `[EmailTemplateService.cs:EmailTemplateService]` - Synchronously renders templates and normalizes recipient email addresses (trim and lowercase) at enqueue time.
- `[TemplatedEmailQueue.cs:TemplatedEmailQueue.TryEnqueueTemplate]` - Renders template synchronously at enqueue time; immediately returns `false` if bounded queue is full.
- `[LoginAttemptTracker.cs:LoginAttemptTracker.EvaluateAsync]` - Throttles logins if email-wide recent failure count reaches or exceeds 30 within `AuthRules.LoginFailureWindow` (mitigating distributed brute force attacks).
- `[SecurityAuditWriter.cs:SecurityAuditWriter.Redact]` - Recursively redacts sensitive payload properties (`password_hash`, `otp_hash`, `refresh_token_hash`, `previous_refresh_token_hash`, `token_hash`, `password`, `otp`, `refresh_token`).
- `[SessionReconciliationService.cs:SessionReconciliationService.ReconcileAfterMultiSessionLossAsync]` - Acquires database transactional advisory lock before revoking non-survivor sessions and enabling `enforce_single_session` to prevent partial unique index violations.

## Side Effects
- `[AuthCleanupWorker.cs:AuthCleanupWorker.RunCleanupAsync]` - Deletes stale sessions (>30 days old), expired pending registrations, stale password reset tokens (>7 days old), stale login attempts (>24 hours old), and old security events (>30 days old) from the database.
- `[ChannelEmailQueue.cs:ChannelEmailQueue.TryEnqueue]` - Writes `EmailMessage` to channel and increments metrics.
- `[EmailSenderWorker.cs:EmailSenderWorker.ExecuteAsync]` - Dispatches messages from queue, logs warnings on retry, persists permanently failed messages to `dead_letter_emails` table, publishes `EmailDeliveryFailedNotification`, and increments metric counters (`email_queue.sent`, `email_queue.retry`, `email_queue.dead_letter`).
- `[SmtpEmailSender.cs:SmtpEmailSender.SendAsync]` - Dispatches multipart/alternative MIME messages to remote SMTP server over encrypted TLS connection.
- `[LoginAttemptTracker.cs:LoginAttemptTracker.RegisterFailureAsync]` - Adds or updates `LoginAttempt` entity via repository.
- `[LoginAttemptTracker.cs:LoginAttemptTracker.ResetAsync]` - Updates `LoginAttempt` entity resetting failure counts.
- `[RbacInvalidationToken.cs:RbacInvalidationToken.Invalidate]` - Cancels and recreates internal `CancellationTokenSource`.
- `[SecurityAuditWriter.cs:SecurityAuditWriter.WriteAsync]` - Increments metric counter, creates a new `AppDbContext` instance, stages `SecurityEvent`, and persists it to the PostgreSQL database.
- `[SessionCacheHook.cs:MemorySessionCacheHook.Evict]` - Mutates in-memory eviction set.
- `[SessionReconciliationService.cs:SessionReconciliationService.ReconcileAfterMultiSessionLossAsync]` - Acquires PostgreSQL advisory transaction lock, revokes sessions, enqueues post-commit cache evictions, and updates single-session enforcement in the database.

## Verification
- Run: `dotnet test`
