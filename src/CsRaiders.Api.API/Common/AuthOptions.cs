namespace CsRaiders.Api.API.Common;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public Dictionary<int, string> PasswordPepper { get; init; } = [];
    public string OtpHmacKey { get; init; } = string.Empty;
    public string JwtSigningKey { get; init; } = string.Empty;
    public string RefreshCookieName { get; init; } = "__Host-csraiders_rt";
    public string DeviceFingerprintCookieName { get; init; } = "csraiders_dfp";
    public SiteOptions Site { get; init; } = new();
    public ProxyOptions Proxy { get; init; } = new();
    public RateLimitOptions RateLimit { get; init; } = new();
    public EmailQueueOptions EmailQueue { get; init; } = new();
    public CryptoOptions Crypto { get; init; } = new();
}

public sealed class SiteOptions
{
    public string Origin { get; init; } = string.Empty;
}

public sealed class ProxyOptions
{
    public string[] KnownProxies { get; init; } = [];
}

public sealed class RateLimitOptions
{
    public int PerMinutePerIp { get; init; } = 20;
}

public sealed class EmailQueueOptions
{
    public int Capacity { get; init; } = 1000;
}

public sealed class CryptoOptions
{
    public int SemaphoreTimeoutSeconds { get; init; } = 2;
}
