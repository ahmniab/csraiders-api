namespace CsRaiders.Api.Infrastructure.Services;

using CsRaiders.Api.Application.Common.Interfaces.Security;

public sealed class MemorySessionCacheHook : ISessionCacheHook
{
    private readonly object sync = new();
    private readonly HashSet<Guid> evicted = [];
    public void Evict(Guid sessionId) { lock (sync) evicted.Add(sessionId); }
    public bool WasEvicted(Guid sessionId) { lock (sync) return evicted.Contains(sessionId); }
}
