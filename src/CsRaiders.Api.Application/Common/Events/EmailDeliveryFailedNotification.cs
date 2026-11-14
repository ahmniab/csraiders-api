using CsRaiders.Api.Domain.Entities;
using Mediator;

namespace CsRaiders.Api.Application.Common.Events;

public sealed record EmailDeliveryFailedNotification(
    EmailMessage Message,
    string ErrorReason,
    int AttemptCount,
    DateTimeOffset FailedAt) : INotification;
