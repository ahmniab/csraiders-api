using System.Net;
using System.Text.Json;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface ISecurityEventRepository
{
    Task AddAsync(string eventName, Guid? userId = null, Email? email = null, IPAddress? ip = null,
        JsonDocument? details = null, DateTimeOffset? createdAt = null, CancellationToken ct = default);
    Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
