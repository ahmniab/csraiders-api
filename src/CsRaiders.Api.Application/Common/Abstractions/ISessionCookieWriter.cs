namespace CsRaiders.Api.Application.Common.Abstractions;

public sealed record SessionCookie(string Name, string Value, TimeSpan MaxAge);

public interface ISessionCookieWriter
{
    string GetOrCreateDeviceFingerprint(string? existing);
    IReadOnlyList<SessionCookie> CreateLoginCookies(string refreshToken, string deviceFingerprint, bool includeDevice);
}
