namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface ISessionCacheHook
{
    void Evict(Guid sessionId);
}
