using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CsRaiders.Api.Infrastructure.Interceptors;

/// <summary>Central redaction policy for future EF audit payloads.</summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    { "password_hash", "otp_hash", "refresh_token_hash", "previous_refresh_token_hash", "token_hash", "password", "otp", "refresh_token" };

    public static JsonDocument Redact(JsonDocument document)
    {
        var node = JsonNode.Parse(document.RootElement.GetRawText());
        if (node is JsonObject obj)
            foreach (var property in obj.ToList())
                if (Denied.Contains(property.Key)) obj[property.Key] = "[REDACTED]";
        return JsonDocument.Parse(node?.ToJsonString() ?? "{}");
    }
}
