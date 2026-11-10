namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface ITokenService
{
    string CreateAccessToken(Guid userId, Guid sessionId, DateTimeOffset now);
    string CreateRefreshToken(Guid userId, Guid sessionId, DateTimeOffset now);
    bool TryValidateRefreshToken(string token, DateTimeOffset now, out RefreshTokenClaims claims);
    byte[] HashRefreshToken(string token);
}

public sealed record RefreshTokenClaims(Guid UserId, Guid SessionId, Guid TokenId);
