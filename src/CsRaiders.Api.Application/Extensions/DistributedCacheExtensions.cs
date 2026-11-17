using System.Text.Json;
using CsRaiders.Api.Domain.Entities;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Extensions;

public static class DistributedCacheExtensions
{
    public static async Task 
        SetObjectAsync<T>(this IDistributedCache cache, 
            string key,
            T data,
        DistributedCacheEntryOptions options)
    {
        await cache.SetStringAsync(key, 
            JsonSerializer.Serialize(data), options);
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
            JsonSerializer.Serialize(data), 
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
            return JsonSerializer.Deserialize<T>(objectJson);
        }
        catch (Exception e)
        {
            return null;
        }
        
    }
    
    public static async Task<Session?> GetSessionAsync(
        this IDistributedCache cache, string id)
    {
        return await cache.GetObjectAsync<Session>($"sid:{id}");
    }
    
    public static async Task SetSessionAsync(
        this IDistributedCache cache, Session session)
    {
        await cache.SetObjectAsync($"sid:{session.Id}", session);
    }

}