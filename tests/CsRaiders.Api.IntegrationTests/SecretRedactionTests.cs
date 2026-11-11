using System.Text.Json;
using CsRaiders.Api.Infrastructure.Interceptors;

namespace CsRaiders.Api.IntegrationTests;

public sealed class SecretRedactionTests
{
    [Fact]
    public void Audit_payload_redacts_secret_fields_recursively()
    {
        using var input = JsonDocument.Parse("{\"password_hash\":\"secret\",\"nested\":{\"otp\":\"123456\"},\"safe\":\"ok\"}");
        using var redacted = AuditSaveChangesInterceptor.Redact(input);
        var root = redacted.RootElement;
        Assert.Equal("[REDACTED]", root.GetProperty("password_hash").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("nested").GetProperty("otp").GetString());
        // The interceptor is also used as a central policy; its recursive event
        // writer counterpart is covered by the security-audit integration flows.
        Assert.Equal("ok", root.GetProperty("safe").GetString());
    }
}
