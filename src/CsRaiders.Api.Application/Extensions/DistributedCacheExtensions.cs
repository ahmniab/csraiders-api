using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Extensions;

public static class DistributedCacheExtensions
{
    /// <summary>
    /// Shared options. Includes an IPAddress converter: System.Text.Json has no built-in one and,
    /// without it, reflection serializes IPAddress.ScopeId whose getter performs a socket call and
    /// throws "Operation not supported" (SocketException 95) on Linux for IPv6 addresses.
    /// </summary>
    private static readonly JsonSerializerOptions CacheJson = new()
    {
        Converters = { new IPAddressJsonConverter() }
    };

    public static async Task
        SetObjectAsync<T>(this IDistributedCache cache,
            string key,
            T data,
        DistributedCacheEntryOptions options)
    {
        await cache.SetStringAsync(key,
            JsonSerializer.Serialize(data, CacheJson), options);
    }

    public static async Task
        SetObjectAsync<T>(this IDistributedCache cache,
            string key,
            T data,
            int minutesTtl = 5)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow
                = TimeSpan.FromMinutes(minutesTtl)

        };
        await cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(data, CacheJson),
            options);
    }

    public static async Task<T?> GetObjectAsync<T>(
        this IDistributedCache cache, string key) where T : class
    {
        var objectJson = await cache.GetStringAsync(key);
        if (string.IsNullOrWhiteSpace(objectJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(objectJson, CacheJson);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<SessionStatus?> GetSessionStatusAsync(
        this IDistributedCache cache, string id)
    {
        return await cache.GetObjectAsync<SessionStatus>($"sid:{id}");
    }

    public static async Task SetSessionStatusAsync(
        this IDistributedCache cache, SessionStatus status)
    {
        await cache.SetObjectAsync($"sid:{status.SessionId}", status);
    }
}

/// <summary>Serializes an IPAddress as its string form ("192.168.0.1", "::1", "fe80::1%eth0").</summary>
file sealed class IPAddressJsonConverter : JsonConverter<IPAddress>
{
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType is JsonTokenType.Null
            ? null!
            : IPAddress.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
