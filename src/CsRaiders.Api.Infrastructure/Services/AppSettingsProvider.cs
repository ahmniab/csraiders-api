using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Constants;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class AppSettingsProvider(IAppSettingRepository settings) : IAppSettingsProvider
{
    private const string Key = "session.multi_max_concurrent";
    private readonly object sync = new();
    private (int Value, DateTimeOffset Expires)? cached;

    public async Task<int> GetMultiSessionCapAsync(CancellationToken ct = default)
    {
        lock (sync)
            if (cached is { } value && value.Expires > DateTimeOffset.UtcNow) return value.Value;

        var raw = await settings.GetValueAsync(Key, ct);
        var resolved = int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : AuthRules.DefaultMultiSessionCap;
        lock (sync) cached = (resolved, DateTimeOffset.UtcNow.AddSeconds(5));
        return resolved;
    }

    public void EvictMultiSessionCap()
    {
        lock (sync) cached = null;
    }
}
