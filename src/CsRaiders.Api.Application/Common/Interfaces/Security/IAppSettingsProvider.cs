namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface IAppSettingsProvider
{
    Task<int> GetMultiSessionCapAsync(CancellationToken ct = default);
    void EvictMultiSessionCap();
}
