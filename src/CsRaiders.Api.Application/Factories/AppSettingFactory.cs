using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="AppSetting"/>. Mutation lives in <see cref="Extensions.AppSettingExtensions"/>.</summary>
public static class AppSettingFactory
{
    public static AppSetting Create(string key, string value, DateTimeOffset now) => new()
    {
        Key = key,
        Value = value,
        UpdatedAt = now
    };
}
