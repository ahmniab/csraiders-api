using System.Net;
using System.Text.Json;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Domain.Entities;

public sealed class SecurityEvent : Entity
{
    public string EventName { get; set; } = null!;
    public Guid? UserId { get; set; }
    public Email? Email { get; set; }
    public IPAddress? Ip { get; set; }
    public JsonDocument Details { get; set; } = JsonDocument.Parse("{}");
    public DateTimeOffset CreatedAt { get; set; }
}
