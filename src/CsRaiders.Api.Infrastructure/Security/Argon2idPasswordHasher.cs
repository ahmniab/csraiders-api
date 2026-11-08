using System.Security.Cryptography;
using System.Text;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using Microsoft.Extensions.Configuration;
using Konscious.Security.Cryptography;

namespace CsRaiders.Api.Infrastructure.Security;

public sealed class Argon2idPasswordHasher(IConfiguration configuration) : IPasswordHasher
{
    private const int MemoryKb = 19456;
    private readonly SemaphoreSlim gate = new(Math.Max(1, Environment.ProcessorCount));
    private readonly int timeoutSeconds = int.TryParse(configuration["Crypto:SemaphoreTimeoutSeconds"], out var configuredTimeout) ? configuredTimeout : 2;
    private readonly int activePepper = configuration.GetSection("Auth:PasswordPepper").GetChildren().Select(x => int.TryParse(x.Key, out var n) ? n : 1).DefaultIfEmpty(1).Max();
    private readonly IConfiguration config = configuration;

    public async Task<PasswordHashResult> HashAsync(string password, CancellationToken ct = default)
    {
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct)) return PasswordHashResult.Unavailable;
        try
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = await DeriveAsync(Pepper(password, activePepper), salt, ct);
            return PasswordHashResult.Succeeded($"$argon2id$v=19$m={MemoryKb},t=2,p=1${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}$p={activePepper}");
        }
        finally { gate.Release(); }
    }

    public async Task<PasswordVerifyResult> VerifyAsync(string password, string? encodedHash, CancellationToken ct = default)
    {
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct))
            return new(false, false, HashOutcome.Unavailable);
        try
        {
            var parsed = Parse(encodedHash);
            var salt = parsed?.Salt ?? RandomNumberGenerator.GetBytes(16);
            var expected = parsed?.Hash ?? Array.Empty<byte>();
            var actual = await DeriveAsync(Pepper(password, parsed?.Pepper ?? activePepper), salt, ct);
            var valid = expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
            return new(valid, valid && parsed is not null && parsed.Value.Pepper != activePepper);
        }
        finally { gate.Release(); }
    }

    private static async Task<byte[]> DeriveAsync(string password, byte[] salt, CancellationToken ct)
    {
        var argon = new Argon2id(Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)))
        {
            Salt = salt, MemorySize = MemoryKb, Iterations = 2, DegreeOfParallelism = 1
        };
        ct.ThrowIfCancellationRequested();
        return await argon.GetBytesAsync(32);
    }

    private string Pepper(string password, int version)
    {
        var key = config[$"Auth:PasswordPepper:{version}"] ?? string.Empty;
        return Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC))));
    }

    private static (byte[] Salt, byte[] Hash, int Pepper)? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || !parts[0].Equals("argon2id", StringComparison.Ordinal)) return null;
        var pepperPart = parts.LastOrDefault(x => x.StartsWith("p=", StringComparison.Ordinal));
        return (Convert.FromBase64String(parts[3]), Convert.FromBase64String(parts[4]),
            pepperPart is not null && int.TryParse(pepperPart[2..], out var p) ? p : 1);
    }
}
