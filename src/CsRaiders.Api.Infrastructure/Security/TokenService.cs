using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CsRaiders.Api.Infrastructure.Security;

public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    public string CreateAccessToken(Guid userId, Guid sessionId, DateTimeOffset now)
    {
        var origin = configuration["Site:Origin"] ?? string.Empty;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:JwtSigningKey"] ?? string.Empty));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new(JwtRegisteredClaimNames.Sub, userId.ToString()), new("sid", sessionId.ToString())]),
            IssuedAt = now.UtcDateTime, Expires = now.Add(AuthRules.AccessTokenLifetime).UtcDateTime,
            SigningCredentials = credentials, Issuer = origin, Audience = origin
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(descriptor));
    }
    public string CreateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public byte[] HashRefreshToken(string token) => SHA256.HashData(Base64UrlEncoder.DecodeBytes(token));
}
