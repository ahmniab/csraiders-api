namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface ITokenService
{
    string CreateAccessToken(Guid userId, Guid sessionId, DateTimeOffset now);
    string CreateRefreshToken();
    byte[] HashRefreshToken(string token);
}
