using System.Net;
using System.Text.Json;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence.Repositories;

public sealed class SecurityEventRepository(AppDbContext db) : ISecurityEventRepository
{
    public async Task AddAsync(string eventName, Guid? userId = null, Email? email = null, IPAddress? ip = null,
        JsonDocument? details = null, DateTimeOffset? createdAt = null, CancellationToken ct = default)
    {
        await db.SecurityEvents.AddAsync(SecurityEvent.Create(eventName, userId, email, ip,
            details ?? JsonDocument.Parse("{}"), createdAt ?? DateTimeOffset.UtcNow), ct);
    }

    public Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        db.SecurityEvents.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
}

public sealed class AppSettingRepository(AppDbContext db) : IAppSettingRepository
{
    public Task<AppSetting?> GetAsync(string key, CancellationToken ct = default) =>
        db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);

    public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
        db.AppSettings.Where(x => x.Key == key).Select(x => (string?)x.Value).SingleOrDefaultAsync(ct);

    public async Task SetAsync(string key, string value, DateTimeOffset now, CancellationToken ct = default)
    {
        var setting = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (setting is null) await db.AppSettings.AddAsync(AppSetting.Create(key, value, now), ct);
        else setting.SetValue(value, now);
    }
}
