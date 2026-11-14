# Implementation Plan: Real Email Delivery & Service Templating

**Branch**: `003-email-service-templating` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-email-service-templating/spec.md`

## Summary

Implement a production-grade, object-oriented email subsystem for the CS Raiders platform:
1. **Real Mail Transport**: An abstracted, MailKit-powered secure SMTP delivery service (`SmtpEmailSender`) supporting TLS encryption, configurable credentials, sender identity, and multi-part MIME (rich HTML + plain text), alongside configurable non-production modes (`ConsoleEmailSender` and mock sinks).
2. **Standardized Service Templating**: An extensible template engine (`IEmailTemplateService`) with strongly typed template definitions (`IEmailTemplate<TModel>`) and built-in templates for core identity workflows (Registration OTP, Password Reset, and Security Alerts), featuring dynamic parameter sanitization against HTML injection.
3. **Ergonomic Queue Integration**: A non-blocking queueing extension (`emailQueue.TryEnqueueTemplate(...)` and `ITemplatedEmailQueue` facade) enabling calling services (e.g., `RegisterCommandHandler`, `ForgotPasswordCommandHandler`) to render and enqueue emails in a single atomic method call with `bool` backpressure semantics.
4. **Resilience & Dead-Letter Storage**: Integration with the background worker (`EmailSenderWorker`) providing exponential backoff retries (0s, 1s, 5s), log sanitization (masked recipients, zero OTP/body leaks), failure metrics, and durable dead-letter database storage (`DeadLetterEmail` in PostgreSQL) coupled with in-process event emission (`EmailDeliveryFailedNotification`) on retry exhaustion.

---

## Technical Context

**Language/Version**: C# / .NET 10.0 (pinned SDK 10.0.113, ASP.NET Core 10 hosting model).

**Primary Dependencies**:
- `MailKit` + `MimeKit` (modern, RFC-compliant SMTP client and MIME composition recommended by Microsoft).
- `EF Core 10.0.14` + `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3` + `EFCore.NamingConventions 10.0.1` (existing persistence stack).
- `MediatR 14.1.0` (in-process event publishing for delivery failure notifications).
- `System.Threading.Channels` (existing in-process bounded channel queue).
- `xUnit` + `FluentAssertions` + `Moq` (unit and integration tests).

**Storage**: PostgreSQL 16 (new `dead_letter_emails` table with `citext` recipient and snake_case columns).

**Testing**: xUnit unit tests in `CsRaiders.Api.Domain.UnitTests`, `CsRaiders.Api.Application.UnitTests`, and integration tests in `CsRaiders.Api.IntegrationTests`.

**Target Platform**: Linux host / Docker container behind Nginx reverse proxy → Kestrel.

**Project Type**: Layered N-tier solution: `Domain` ← `Application` ← `Infrastructure` ← `API`.

**Performance Goals**:
- Template generation latency: < 1 ms.
- Queue enqueue latency: < 100 µs (non-blocking in-memory channel write).
- SMTP delivery latency: < 5 s for 99% of valid delivery requests.

**Constraints**:
- Outbound-only transactional emails; binary file attachments and inbound bounce webhooks are explicitly out of scope.
- Single-language English templates with extensible interface design for future localization.
- Zero credentials or one-time verification codes in log outputs.
- Non-blocking queue saturation behavior (returns `false` immediately on backpressure).

**Scale/Scope**: Educational platform supporting thousands of active students; ~12 new or updated production files across Domain, Application, and Infrastructure layers.

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Status**: Verified against core principles in `.specify/memory/constitution.md` and repository architectural standards.

1. **Test-First & Verifiable**: All user journeys (SMTP delivery, template rendering, queue extension, dead-letter storage) map to verifiable test scenarios in `quickstart.md`. **PASS**
2. **Layering & Separation of Concerns**:
   - `Domain`: Pure models (`EmailMessage`, `DeadLetterEmail`, `EmailPurpose`). No external dependencies.
   - `Application`: Interfaces (`IEmailTemplate<TModel>`, `IEmailTemplateService`, `ITemplatedEmailQueue`, `IDeadLetterEmailRepository`), template models, and event definitions.
   - `Infrastructure`: `MailKit` implementation (`SmtpEmailSender`), `ChannelEmailQueue`, `EmailSenderWorker`, EF Core entity configuration, and repository persistence.
   - `API`: Configuration binding and DI registration. **PASS**
3. **No Secrets in Artifacts & Code**: Sensitive credentials (SMTP username/password) are loaded from configuration / environment variables; zero hardcoded secrets. Log outputs redact recipient addresses and omit sensitive OTP payloads. **PASS**

---

## Project Structure

### Documentation (this feature)

```text
specs/003-email-service-templating/
├── spec.md              # Feature specification
├── plan.md              # Implementation Plan (this file)
├── research.md          # Phase 0 decisions (D1–D7)
├── data-model.md        # Phase 1 data model & state machines
├── quickstart.md        # Phase 1 end-to-end validation guide
├── contracts/           # Phase 1 contracts
│   └── email-contracts.md # C# interface contracts and signatures
└── checklists/
    └── requirements.md  # Spec quality checklist (16/16 pass)
```

### Source Code (repository root layout)

```text
src/
├── CsRaiders.Api.Domain/
│   └── Entities/
│       ├── EmailMessage.cs                  # [MODIFY] Add BodyHtml, BodyText support
│       └── DeadLetterEmail.cs               # [NEW] Dead-letter entity for failed deliveries
├── CsRaiders.Api.Application/
│   ├── Common/
│   │   ├── Interfaces/
│   │   │   ├── Security/
│   │   │   │   ├── IEmailTemplate.cs        # [NEW] Template strategy interface
│   │   │   │   ├── IEmailTemplateService.cs # [NEW] Template renderer service interface
│   │   │   │   └── ITemplatedEmailQueue.cs  # [NEW] Queue extension & facade interface
│   │   │   └── Repositories/
│   │   │       └── IDeadLetterEmailRepository.cs # [NEW] Dead-letter repository contract
│   │   ├── EmailTemplates/
│   │   │   ├── Layouts/HtmlEmailLayout.cs   # [NEW] Shared responsive HTML email wrapper
│   │   │   ├── Models/                      # [NEW] Strongly-typed template parameter records
│   │   │   │   ├── RegistrationOtpTemplateModel.cs
│   │   │   │   ├── PasswordResetTemplateModel.cs
│   │   │   │   └── SecurityNoticeTemplateModel.cs
│   │   │   └── Templates/                   # [NEW] Built-in templates
│   │   │       ├── RegistrationOtpTemplate.cs
│   │   │       ├── PasswordResetTemplate.cs
│   │   │       └── SecurityNoticeTemplate.cs
│   │   └── Events/
│   │       └── EmailDeliveryFailedNotification.cs # [NEW] Delivery failure event
│   └── Features/Auth/Commands/
│       ├── Register/RegisterCommand.cs      # [MODIFY] Adopt templated queue helper
│       └── ForgotPassword/ForgotPasswordCommand.cs # [MODIFY] Adopt templated queue helper
├── CsRaiders.Api.Infrastructure/
│   ├── CsRaiders.Api.Infrastructure.csproj  # [MODIFY] Add MailKit package reference
│   ├── Options/
│   │   └── EmailOptions.cs                  # [NEW] Strongly typed SMTP/delivery options
│   ├── Services/
│   │   ├── EmailTemplateService.cs          # [NEW] Implementation of IEmailTemplateService
│   │   ├── TemplatedEmailQueue.cs           # [NEW] Facade and extension helpers
│   │   ├── SmtpEmailSender.cs               # [NEW] MailKit SMTP delivery implementation
│   │   └── EmailSenderWorker.cs             # [MODIFY] Dead-letter persistence & event publishing
│   ├── Persistence/
│   │   ├── AppDbContext.cs                  # [MODIFY] Add DeadLetterEmails DbSet
│   │   ├── Configurations/
│   │   │   └── DeadLetterEmailConfiguration.cs # [NEW] EF Core entity configuration
│   │   └── Repositories/
│   │       └── DeadLetterEmailRepository.cs # [NEW] Repository implementation
│   └── DependencyInjection/
│       └── InfrastructureServiceExtensions.cs # [MODIFY] Register EmailOptions, SmtpEmailSender, template services
tests/
├── CsRaiders.Api.Application.UnitTests/
│   ├── EmailTemplates/                      # [NEW] Template rendering & HTML sanitization tests
│   └── Security/                            # [NEW] Templated queue extension tests
├── CsRaiders.Api.Domain.UnitTests/
│   └── Entities/                            # [NEW] DeadLetterEmail & EmailMessage unit tests
└── CsRaiders.Api.IntegrationTests/
    └── Email/                               # [NEW] Smtp delivery & dead-letter persistence tests
```

---

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| None | Clean architecture conventions preserved; no unjustified complexity introduced. | N/A |
