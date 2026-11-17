using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Extensions;

public static class AppSettingExtensions
{
    public static void SetValue(this AppSetting setting, string value, DateTimeOffset now)
    {
        setting.Value = value;
        setting.UpdatedAt = now;
    }
}
