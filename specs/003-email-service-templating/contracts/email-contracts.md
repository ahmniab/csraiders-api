# Interface Contracts: Real Email Delivery & Service Templating

**Feature**: `003-email-service-templating` | **Date**: 2026-11-13

This document defines the formal C# interface contracts and signatures implemented across the Domain, Application, and Infrastructure layers.

---

## 1. Domain & Application Abstractions

### `IEmailTemplate<in TModel>`

```csharp
namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface IEmailTemplate<in TModel>
{
    EmailPurpose Purpose { get; }
    string BuildSubject(TModel model);
    string RenderHtml(TModel model);
    string RenderText(TModel model);
}
```

### `IEmailTemplateService`

```csharp
namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public sealed record EmailContent(string Subject, string BodyHtml, string BodyText);

public interface IEmailTemplateService
{
    EmailContent Render<TModel>(IEmailTemplate<TModel> template, TModel model);
    EmailMessage CreateMessage<TModel>(
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null);
}
```

---

## 2. Queue Ergonomics & Extension Contracts

### `TemplatedEmailQueueExtensions`

Provides a high-level helper directly extending `IEmailQueue`:

```csharp
namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public static class TemplatedEmailQueueExtensions
{
    public static bool TryEnqueueTemplate<TModel>(
        this IEmailQueue queue,
        IEmailTemplateService templateService,
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null)
    {
        var message = templateService.CreateMessage(recipient, template, model, correlationId);
        return queue.TryEnqueue(message);
    }
}
```

### `ITemplatedEmailQueue`

Convenience facade for injecting a single unified queue service:

```csharp
namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface ITemplatedEmailQueue
{
    bool TryEnqueueTemplate<TModel>(
        string recipient,
        IEmailTemplate<TModel> template,
        TModel model,
        string? correlationId = null);

    bool TryEnqueue(EmailMessage message);
}
```

---

## 3. Delivery Transport & Dead-Letter Contracts

### `IEmailSender`

```csharp
namespace CsRaiders.Api.Infrastructure.Services;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
```

### `IDeadLetterEmailRepository`

```csharp
namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface IDeadLetterEmailRepository
{
    Task AddAsync(DeadLetterEmail deadLetter, CancellationToken ct = default);
    Task<IReadOnlyList<DeadLetterEmail>> GetRecentAsync(int count = 50, CancellationToken ct = default);
}
```

### `EmailDeliveryFailedNotification` (Application Event)

```csharp
namespace CsRaiders.Api.Application.Common.Events;

using MediatR;
using CsRaiders.Api.Domain.Entities;

public sealed record EmailDeliveryFailedNotification(
    EmailMessage Message,
    string ErrorReason,
    int AttemptCount,
    DateTimeOffset FailedAt) : INotification;
```

---

## 4. Built-In Template Signatures

Standard templates provided out of the box in `CsRaiders.Api.Application.Common.EmailTemplates`:

1. `RegistrationOtpTemplate : IEmailTemplate<RegistrationOtpTemplateModel>`
   - `Purpose`: `EmailPurpose.RegistrationOtp`
   - `Subject`: `"Verify your CS Raiders account"`
2. `PasswordResetTemplate : IEmailTemplate<PasswordResetTemplateModel>`
   - `Purpose`: `EmailPurpose.PasswordReset`
   - `Subject`: `"Reset your CS Raiders password"`
3. `SecurityNoticeTemplate : IEmailTemplate<SecurityNoticeTemplateModel>`
   - `Purpose`: `EmailPurpose.AccountNotice`
   - `Subject`: Dynamic from `model.Title` (`"Security Alert: {model.Title}"`)
