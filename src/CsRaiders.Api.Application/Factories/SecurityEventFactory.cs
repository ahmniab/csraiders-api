using System.Net;
using System.Text.Json;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="SecurityEvent"/>.</summary>
public static class SecurityEventFactory
{
    public static SecurityEvent Create(string eventName, Guid? userId, Email? email, IPAddress? ip,
        JsonDocument details, DateTimeOffset now) => new()
    {
        EventName = eventName,
        UserId = userId,
        Email = email,
        Ip = ip,
        Details = details,
        CreatedAt = now
    };
}
