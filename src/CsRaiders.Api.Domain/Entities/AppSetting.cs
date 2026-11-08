namespace CsRaiders.Api.Domain.Entities;

public sealed class AppSetting
{
    private AppSetting() { }

    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AppSetting Create(string key, string value, DateTimeOffset now) => new()
    {
        Key = key,
        Value = value,
        UpdatedAt = now
    };

    public void SetValue(string value, DateTimeOffset now)
    {
        Value = value;
        UpdatedAt = now;
    }
}
