using System.Security.Cryptography;
using System.Text;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using Microsoft.Extensions.Configuration;

namespace CsRaiders.Api.Infrastructure.Security;

public sealed class HmacOtpGenerator(IConfiguration configuration) : IOtpGenerator
{
    private readonly byte[] key = Encoding.UTF8.GetBytes(configuration["Auth:OtpHmacKey"] ?? string.Empty);
    public string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    public byte[] Protect(string email, string otp) => Hmac(email, otp);
    public bool Verify(string email, string otp, byte[] expected) => CryptographicOperations.FixedTimeEquals(Hmac(email, otp), expected);
    private byte[] Hmac(string email, string otp) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{email}:{otp}"));
}
