using System.Net;
using System.Text.Json;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface ISecurityAuditWriter
{
    Task WriteAsync(string eventName, Guid? userId = null, Email? email = null, IPAddress? ip = null,
        object? details = null, CancellationToken ct = default);
}
