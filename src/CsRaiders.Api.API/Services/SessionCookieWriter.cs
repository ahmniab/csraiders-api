using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using CsRaiders.Api.Application.Common.Abstractions;

namespace CsRaiders.Api.API.Services;

public sealed class SessionCookieWriter : ISessionCookieWriter
{
    public const string RefreshCookie = "__Host-csraiders_rt";
    public const string DeviceCookie = "csraiders_dfp";

    public string GetOrCreateDeviceFingerprint(string? existing) =>
        !string.IsNullOrWhiteSpace(existing) ? existing
            : Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public IReadOnlyList<SessionCookie> CreateLoginCookies(string refreshToken, string deviceFingerprint, bool includeDevice) =>
        includeDevice
            ? [new(RefreshCookie, refreshToken, TimeSpan.FromDays(30)), new(DeviceCookie, deviceFingerprint, TimeSpan.FromDays(365))]
            : [new(RefreshCookie, refreshToken, TimeSpan.FromDays(30))];

    public SessionCookie CreateRefreshCookie(string refreshToken) => new(RefreshCookie, refreshToken, TimeSpan.FromDays(30));
    public SessionCookie CreateClearRefreshCookie() => new(RefreshCookie, string.Empty, TimeSpan.Zero);
    public SessionCookie CreateClearDeviceCookie() => new(DeviceCookie, string.Empty, TimeSpan.Zero);
}
