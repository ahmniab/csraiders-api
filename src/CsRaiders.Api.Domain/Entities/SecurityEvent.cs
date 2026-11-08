using System.Net;
using System.Text.Json;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Domain.Entities;

public sealed class SecurityEvent : Entity
{
    private SecurityEvent() { }

    public string EventName { get; private set; } = null!;
    public Guid? UserId { get; private set; }
    public Email? Email { get; private set; }
    public IPAddress? Ip { get; private set; }
    public JsonDocument Details { get; private set; } = JsonDocument.Parse("{}");
    public DateTimeOffset CreatedAt { get; private set; }

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
