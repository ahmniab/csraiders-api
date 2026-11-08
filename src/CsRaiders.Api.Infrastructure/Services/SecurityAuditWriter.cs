using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Infrastructure.Persistence;
using CsRaiders.Api.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class SecurityAuditWriter(IDbContextFactory<AppDbContext> factory) : ISecurityAuditWriter
{
    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    { "password_hash", "otp_hash", "refresh_token_hash", "previous_refresh_token_hash", "token_hash", "password", "otp", "refresh_token" };

    public async Task WriteAsync(string eventName, Guid? userId = null, Email? email = null, IPAddress? ip = null,
        object? details = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var node = details is null ? new JsonObject() : JsonSerializer.SerializeToNode(details);
        Redact(node);
        db.SecurityEvents.Add(CsRaiders.Api.Domain.Entities.SecurityEvent.Create(eventName, userId, email, ip,
            JsonDocument.Parse(node?.ToJsonString() ?? "{}"), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    private static void Redact(JsonNode? node)
    {
        if (node is not JsonObject obj) return;
        foreach (var property in obj.ToList())
        {
            if (Denied.Contains(property.Key)) obj[property.Key] = "[REDACTED]";
            else Redact(property.Value);
        }
    }
}
