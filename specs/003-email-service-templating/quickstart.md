# Quickstart Validation Guide: Real Email Delivery & Service Templating

**Feature**: `003-email-service-templating` | **Date**: 2026-11-13

This guide provides runnable scenarios to validate that the real email delivery, template generator, and queue extension work end-to-end.

---

## Prerequisites

1. **.NET 10 SDK** (`dotnet --version` >= 10.0.100).
2. **PostgreSQL** running locally or via Docker Testcontainers.
3. Access to an SMTP test server (e.g. Mailpit/MailHog on `localhost:1025`) OR set `Email:DeliveryMode` to `Console` for local terminal validation.

---

## Scenario 1: Verify Template Generation & HTML Sanitization

**Goal**: Confirm that the template service renders rich HTML, plain text, and encodes dynamic user inputs against HTML injection.

```bash
dotnet test --filter "FullyQualifiedName~EmailTemplateServiceTests"
```

**Expected Outcome**:
- Output subject matches template specification.
- Rich HTML body contains required layout and styles.
- Plain text body provides readable fallback without HTML tags.
- Dynamic characters like `<script>` or `&` in models appear escaped as `&lt;script&gt;` and `&amp;` in HTML.

---

## Scenario 2: Validate Direct Templated Enqueueing via Queue Extension

**Goal**: Confirm that calling `emailQueue.TryEnqueueTemplate(...)` or `ITemplatedEmailQueue.TryEnqueueTemplate(...)` renders the template and writes the message to the delivery channel in one operation.

```bash
dotnet test --filter "FullyQualifiedName~TemplatedEmailQueueTests"
```

**Expected Outcome**:
- Returns `true` when channel has capacity.
- Message received by reader contains rendered HTML and plain text matching the template model.
- Returns `false` immediately when channel capacity is saturated (no blocking).

---

## Scenario 3: Real SMTP Delivery with MailKit

**Goal**: Confirm that `SmtpEmailSender` dispatches a multi-part MIME message over SMTP with TLS.

```bash
dotnet test --filter "FullyQualifiedName~SmtpEmailSenderTests"
```

**Expected Outcome**:
- Message accepted by SMTP server.
- Contains both `text/html` and `text/plain` alternative bodies.
- Sender address and display name match `Email:Smtp` configuration.
- Log message indicates success while redacting recipient email and omitting message bodies.

---

## Scenario 4: Retry Exhaustion & Dead-Letter Persistence

**Goal**: Confirm that when all 3 delivery attempts fail, the background worker persists a dead-letter record and raises an alert event.

```bash
dotnet test --filter "FullyQualifiedName~EmailSenderWorkerDeadLetterTests"
```

**Expected Outcome**:
- Worker attempts delivery 3 times with backoff delays (0s, 1s, 5s).
- On final failure:
  - Metric `email_queue.dead_letter` increments by 1.
  - A new row is inserted into `dead_letter_emails` with sanitized error details.
  - `EmailDeliveryFailedNotification` event is published to MediatR handlers.
  - Subsequent queued messages continue to process normally without worker crash.
