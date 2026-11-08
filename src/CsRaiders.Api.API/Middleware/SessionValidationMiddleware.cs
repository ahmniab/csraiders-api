using System.Collections.Concurrent;
using System.Security.Claims;
using CsRaiders.Api.Application.Common.Interfaces.Security;

namespace CsRaiders.Api.API.Middleware;

public sealed class SessionValidationMiddleware(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<Guid, (SessionStatus Status, DateTimeOffset CachedUntil)> Cache = new();

    public async Task InvokeAsync(HttpContext context, ISessionStatusReader reader, ISessionCacheHook cacheHook)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirstValue("sid"), out var sid))
        {
            await next(context);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        SessionStatus? status = null;
        if (Cache.TryGetValue(sid, out var cached) && cached.CachedUntil > now) 
            status = cached.Status;
        else
        {
            status = await reader.ReadAsync(sid, context.RequestAborted);
            if (status is { } found) Cache[sid] = (found, now.AddSeconds(5));
        }

        if (status is null || status.Value.Revoked || status.Value.ExpiresAt <= now || status.Value.IdleExpiresAt <= now)
        {
            Cache.TryRemove(sid, out _);
            cacheHook.Evict(sid);
            context.Response.Cookies.Delete("__Host-csraiders_rt");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new { error = new { code = "session_superseded", message = "The session is no longer active." } });
            return;
        }

        await next(context);
    }
}
