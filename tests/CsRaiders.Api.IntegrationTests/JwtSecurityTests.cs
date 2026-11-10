using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CsRaiders.Api.IntegrationTests;

public sealed class JwtSecurityTests
{
    [Fact]
    public void Jwt_validation_allows_only_hs256_and_expected_issuer_audience()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 32)));
        var token = new JwtSecurityToken(
            issuer: "https://localhost",
            audience: "https://localhost",
            claims: [new Claim("sub", Guid.NewGuid().ToString()), new Claim("sid", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = "https://localhost",
            ValidateAudience = true,
            ValidAudience = "https://localhost",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(
            new JwtSecurityTokenHandler().WriteToken(token), parameters, out _);
        Assert.NotNull(principal.Identity);
    }

    [Fact]
    public void Jwt_validation_rejects_wrong_audience_and_wrong_key()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 32)));
        var token = new JwtSecurityToken("https://localhost", "wrong-audience",
            [new Claim("sub", Guid.NewGuid().ToString())], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var parameters = Validation(key);

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(
            new JwtSecurityTokenHandler().WriteToken(token), parameters, out _));
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(
            new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("https://localhost", "https://localhost",
                [new Claim("sub", Guid.NewGuid().ToString())], expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 32))), SecurityAlgorithms.HmacSha256))), parameters, out _));
    }

    private static TokenValidationParameters Validation(SecurityKey key) => new()
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = key,
        ValidateIssuer = true, ValidIssuer = "https://localhost",
        ValidateAudience = true, ValidAudience = "https://localhost",
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
}
