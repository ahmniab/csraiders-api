using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface IAppSettingRepository
{
    Task<AppSetting?> GetAsync(string key, CancellationToken ct = default);
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, DateTimeOffset now, CancellationToken ct = default);
}
