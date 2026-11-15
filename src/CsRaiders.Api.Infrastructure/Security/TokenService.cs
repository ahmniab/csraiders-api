using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CsRaiders.Api.Infrastructure.Security;

public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    public string CreateAccessToken(Guid userId, Guid sessionId, ulong permissionsBitmask, DateTimeOffset now)
    {
        var origin = configuration["Site:Origin"] ?? string.Empty;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:JwtSigningKey"] ?? string.Empty));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new(JwtRegisteredClaimNames.Sid, sessionId.ToString()),
                new("perms", permissionsBitmask.ToString(CultureInfo.InvariantCulture))
            ]),
            IssuedAt = now.UtcDateTime,
            Expires = now.Add(AuthRules.AccessTokenLifetime).UtcDateTime,
            SigningCredentials = credentials,
            Issuer = origin,
            Audience = origin
        };
        return new JwtSecurityTokenHandler()
            .WriteToken(new JwtSecurityTokenHandler()
                .CreateToken(descriptor));
    }
    public string CreateRefreshToken(Guid userId, Guid sessionId, DateTimeOffset now)
    {
        var origin = configuration["Site:Origin"] ?? string.Empty;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:JwtSigningKey"] ?? string.Empty));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new(JwtRegisteredClaimNames.Sid, sessionId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            IssuedAt = now.UtcDateTime,
            Expires = now.Add(AuthRules.RefreshAbsoluteLifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Issuer = origin,
            Audience = origin
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(descriptor));
    }

    public bool TryValidateRefreshToken(string token, DateTimeOffset now, out RefreshTokenClaims claims)
    {
        claims = default!;
        try
        {
            var origin = configuration["Site:Origin"] ?? string.Empty;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:JwtSigningKey"] ?? string.Empty));
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = origin,
                ValidateAudience = true,
                ValidAudience = origin,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            };
            var principal = handler.ValidateToken(token, parameters, out var validatedToken);
            if (validatedToken is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
                return false;

            var userId = ReadGuid(principal, JwtRegisteredClaimNames.Sub);
            var sessionId = ReadGuid(principal, JwtRegisteredClaimNames.Sid);
            var tokenId = ReadGuid(principal, JwtRegisteredClaimNames.Jti);
            if (userId is null || sessionId is null || tokenId is null) return false;

            var issuedAt = jwt.Payload.Iat;
            if (issuedAt is null || DateTimeOffset.FromUnixTimeSeconds(issuedAt.Value) > now.AddSeconds(30)) return false;

            claims = new RefreshTokenClaims(userId.Value, sessionId.Value, tokenId.Value);
            return true;
        }
        catch (SecurityTokenException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public byte[] HashRefreshToken(string token) =>
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static Guid? ReadGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value) ? value : null;
}
