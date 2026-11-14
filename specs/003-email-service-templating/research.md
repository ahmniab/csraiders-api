# Phase 0: Research & Technical Decisions

**Feature**: `003-email-service-templating` | **Date**: 2026-11-13

## Summary of Decisions

| ID | Topic | Decision | Rationale | Alternatives Considered |
|---|---|---|---|---|
| **D1** | Mail Transport Library | `MailKit` + `MimeKit` | Microsoft officially recommends `MailKit` over obsolete `System.Net.Mail.SmtpClient`. Provides RFC-compliant MIME composition (multipart/alternative for HTML + text), TLS/STARTTLS support, and async I/O with cancellation tokens. | `System.Net.Mail.SmtpClient` (deprecated by Microsoft, buggy TLS/async handling); Cloud provider SDKs (creates vendor lock-in). |
| **D2** | Non-Production Delivery Sink | Configurable Delivery Mode (`SmtpEmailSender` vs `ConsoleEmailSender`) | Application configuration flag (`Email:DeliveryMode` = `Smtp` \| `Console` \| `Disabled`) controls which `IEmailSender` is registered in DI. Allows seamless local development and tests without sending real emails. | Outbox table (unnecessary storage overhead for simple notifications); MailHog mandatory requirement (burdens local developers). |
| **D3** | Template Architecture (OOP) | Strategy / Definition Pattern (`IEmailTemplate<TModel>`) | Strong typing for template parameters with dedicated template classes per email purpose (e.g., `RegistrationOtpTemplate`, `PasswordResetTemplate`, `SecurityNoticeTemplate`). Satisfies Open/Closed and Single Responsibility principles. | Single monolithic switch statement (violates OCP); dynamic untyped dictionaries (lacks compile-time safety). |
| **D4** | Template Engine & Sanitization | Structured Token Replacement with Automatic HTML Encoding | Fast, deterministic placeholder substitution (`{{Name}}`, `{{OtpCode}}`, etc.) inside clean, responsive HTML base layouts and plain-text fallbacks. User input parameters are escaped with `WebUtility.HtmlEncode`. | RazorEngine / Liquid / Scriban (heavy dependencies, reflection overhead, cold-start compilation latency, unnecessary for simple transactional notifications). |
| **D5** | Queue Extension Ergonomics | `IEmailQueue` Extension Methods & `ITemplatedEmailQueue` Facade | Calling services can invoke `emailQueue.TryEnqueueTemplate(to, template, model, correlationId)` in one line. The helper handles template rendering and message composition atomically, returning `bool` on success/capacity. | Manual rendering in each calling command handler (repetitive boilerplate, duplicate validation); Background worker template rendering (requires persisting untyped models). |
| **D6** | Dead-Letter Storage & Alerting | `DeadLetterEmail` Entity in PostgreSQL + `EmailDeliveryFailedEvent` via MediatR | Permanently failed emails (exhausted retries) are stored in a dedicated `dead_letter_emails` table with sanitized error details, and an in-process notification event is published for logging and alerts. | Drop messages after retries (data loss, zero visibility); In-memory buffer only (lost on process restart). |
| **D7** | Privacy & Log Sanitization | Masked Recipient Logging & Redacted Payloads | Recipient addresses in logs are masked (e.g., `j***e@domain.com`); passwords, verification OTPs, and email bodies are excluded from log outputs. Metrics track sent, retry, dropped, and dead-letter counts. | Raw email logging (violates GDPR and exposes security credentials); No delivery logging (prevents production troubleshooting). |

---

## Detailed Decisions

### D1: Mail Transport Library (`MailKit` + `MimeKit`)

- **Context**: The existing implementation uses `ConsoleEmailSender` which logs dummy messages. We need real, production-ready mail delivery via SMTP.
- **Decision**: Introduce `MailKit` package (v4.x) in `CsRaiders.Api.Infrastructure`.
- **Implementation**:
  - `SmtpEmailSender` connects to configured host/port.
  - Supports `SecureSocketOptions.StartTls`, `SslOnConnect`, or `Auto`.
  - Authenticates using username/password if credentials are provided.
  - Constructs `MimeMessage` with `BodyBuilder` containing both `HtmlBody` and `TextBody` (`multipart/alternative`).
  - Implements `IEmailSender.SendAsync(EmailMessage message, CancellationToken ct)`.

### D2: Non-Production Delivery Modes

- **Context**: Local development and automated test suites need to run without requiring a real live SMTP server or risking outbound emails to real addresses.
- **Decision**: Provide `EmailOptions` in `appsettings.json`:
  ```json
  "Email": {
    "DeliveryMode": "Console", // "Smtp", "Console", "Disabled"
    "Smtp": {
      "Host": "smtp.example.com",
      "Port": 587,
      "UseSsl": true,
      "Username": "apikey",
      "Password": "secret-password",
      "SenderEmail": "no-reply@csraiders.com",
      "SenderName": "CS Raiders"
    }
  }
  ```
- **Registration**: If `DeliveryMode == "Smtp"`, register `SmtpEmailSender`; otherwise register `ConsoleEmailSender` (or `NullEmailSender`).

### D3 & D4: Template Service & OOP Design

- **Context**: Multiple services need to send transactional emails (Account verification OTP, Password reset token, Security notice). We must prevent raw HTML string interpolation in command handlers.
- **Decision**:
  - `IEmailTemplate<TModel>` defines `EmailPurpose Purpose`, `string Subject`, `string RenderHtml(TModel model)`, and `string RenderText(TModel model)`.
  - `IEmailTemplateService` maintains registered template definitions and exposes `EmailContent Render<TModel>(IEmailTemplate<TModel> template, TModel model)`.
  - Built-in templates:
    1. `RegistrationOtpTemplate` (Model: `RegistrationOtpModel(string Otp, string DisplayName, int ExpiryMinutes)`)
    2. `PasswordResetTemplate` (Model: `PasswordResetModel(string ResetUrl, string DisplayName, int ExpiryMinutes)`)
    3. `SecurityNoticeTemplate` (Model: `SecurityNoticeModel(string Title, string Message, string DisplayName, DateTimeOffset Timestamp)`)
  - Shared `HtmlEmailLayout`: Generates responsive CSS styling, header, footer, and brand container, wrapping the template content.
  - Sanitization: All string fields within `TModel` are HTML-encoded via `System.Net.WebUtility.HtmlEncode` before insertion into the HTML layout.

### D5: Queue Extension & Caller Ergonomics

- **Context**: Services currently do `emailQueue.TryEnqueue(new EmailMessage(...))` directly. We want consuming services to directly queue templates.
- **Decision**:
  - Extension method:
    ```csharp
    public static bool TryEnqueueTemplate<TModel>(
        this IEmailQueue queue,
        IEmailTemplateService templateService,
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null)
    ```
  - Also provide `ITemplatedEmailQueue` which combines both dependencies for cleaner constructor injection when preferred:
    ```csharp
    public interface ITemplatedEmailQueue
    {
        bool TryEnqueueTemplate<TModel>(string recipient, IEmailTemplate<TModel> template, TModel model, string? correlationId = null);
        bool TryEnqueue(EmailMessage message);
    }
    ```

### D6: Dead-Letter Handling & Event Publication

- **Context**: Requirement FR-015 mandates that permanently failed messages are stored in a durable dead-letter store and emit an in-process notification event.
- **Decision**:
  - Entity `DeadLetterEmail`:
    - `Guid Id` (PK)
    - `string Recipient` (citext / varchar)
    - `string Subject`
    - `string Purpose`
    - `string ErrorReason` (sanitized exception message and classification)
    - `int AttemptCount`
    - `string? CorrelationId`
    - `DateTimeOffset FailedAt`
    - `DateTimeOffset CreatedAt`
  - In `EmailSenderWorker`:
    - On retry exhaustion:
      - Resolve `IDbContextFactory<AppDbContext>` (or `IServiceScopeFactory`) to store `DeadLetterEmail`.
      - Publish `EmailDeliveryFailedNotification(message, ex, attempts)` via `IPublisher` (MediatR).
      - Increment `email_queue.dead_letter` metric.
      - Log warning with sanitized recipient.

### D7: Redaction & Security

- **Decision**:
  - Mask emails: `john.doe@example.com` -> `j***e@example.com`.
  - Never log `message.BodyHtml` or `message.BodyText`.
  - Metric counters:
    - `email_queue.enqueued`
    - `email_queue.sent`
    - `email_queue.retry`
    - `email_queue.dead_letter`
    - `email_queue.dropped`
