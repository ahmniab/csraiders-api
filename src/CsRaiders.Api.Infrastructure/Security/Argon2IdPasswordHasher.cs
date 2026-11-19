using System.Security.Cryptography;
using System.Text;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Konscious.Security.Cryptography;

namespace CsRaiders.Api.Infrastructure.Security;

/// <summary>
/// Argon2id password hasher using the PHC string format:
/// <c>$argon2id$v=19$m={memoryKb},t={iterations},p={parallelism}${saltB64}${hashB64}$p={pepperVersion}</c>
/// New hashes are written with the parameters configured under <c>Crypto:Argon2</c>.
/// Verification always re-derives with the parameters stored in the encoded hash,
/// so parameter changes in configuration never invalidate existing passwords.
/// </summary>
public sealed class Argon2IdPasswordHasher(IConfiguration configuration) : IPasswordHasher
{
    private const int ArgonVersion = 19;
    private const int DefaultMemoryKb = 5 * 1024;   // OWASP-recommended minimum (19 MiB) 19 * 1024 
    private const int DefaultIterations = 2;
    private const int DefaultParallelism = 1;
    private const int HashLength = 32;
    private const int SaltLength = 16;

    private readonly SemaphoreSlim _gate = new(Math.Max(1, Environment.ProcessorCount));
    private readonly int _timeoutSeconds = int.TryParse(configuration["Crypto:SemaphoreTimeoutSeconds"], out var configuredTimeout) ? configuredTimeout : 2;
    private readonly int _activePepper = configuration.GetSection("Auth:PasswordPepper").GetChildren().Select(x => int.TryParse(x.Key, out var n) ? n : 1).DefaultIfEmpty(1).Max();
    private readonly IConfiguration _config = configuration;

    /// <summary>Parameters used for newly created hashes.</summary>
    private readonly HashParams _defaults = new(
        Clamp(
            GetInt(
                configuration, 
                "Crypto:Argon2:MemoryKb", 
                DefaultMemoryKb), 
            8192, 
            1048576
            ),
        Clamp(
            GetInt(
                configuration, 
                "Crypto:Argon2:Iterations", 
                DefaultIterations), 
            1, 
            16
            ),
        Clamp(
            GetInt(
                configuration, 
                "Crypto:Argon2:Parallelism", 
                DefaultParallelism), 
            1, 
            16)
        );

    public async Task<PasswordHashResult> HashAsync(string password, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(_timeoutSeconds), ct)) 
            return PasswordHashResult.Unavailable;
        try
        {
            var (memoryKb, iterations, parallelism) = _defaults;
            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            var hash = await DeriveAsync(
                    Pepper(password, _activePepper), 
                    salt, 
                    memoryKb, 
                    iterations, 
                    parallelism, 
                    ct
                );
            return PasswordHashResult.Succeeded(
                $"$argon2id$v={ArgonVersion}$m={memoryKb},t={iterations},p={parallelism}$" + 
                $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}$p={_activePepper}");
        }
        finally { _gate.Release(); }
    }

    public async Task<PasswordVerifyResult> VerifyAsync(
        string password, 
        string? encodedHash, 
        CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(_timeoutSeconds), ct))
            return new(false, false, HashOutcome.Unavailable);
        try
        {
            var parsed = Parse(encodedHash);
            var salt = parsed?.Salt ?? RandomNumberGenerator.GetBytes(SaltLength);
            var expected = parsed?.Hash ?? Array.Empty<byte>();
            var (memoryKb, iterations, parallelism) = 
                parsed?.Params ?? _defaults;
            
            var actual = 
                await DeriveAsync(
                    Pepper(password, parsed?.Pepper ?? _activePepper), 
                    salt, 
                    memoryKb, 
                    iterations, 
                    parallelism, 
                    ct);
            
            var valid = 
                expected.Length == actual.Length && 
                CryptographicOperations.FixedTimeEquals(expected, actual);
            
            if (!valid || parsed is null) 
                return new(false, false);
            var stale = parsed.Pepper != _activePepper
                || parsed.Params != _defaults;   // legacy parameters -> rehash on next successful login
            return new(valid, stale);
        }
        finally { _gate.Release(); }
    }

    private static async Task<byte[]> DeriveAsync(string password, byte[] salt, int memoryKb, int iterations, int parallelism, CancellationToken ct)
    {
        var argon = new Argon2id(Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        ct.ThrowIfCancellationRequested();
        return await argon.GetBytesAsync(HashLength);
    }

    private string Pepper(string password, int version)
    {
        var key = _config[$"Auth:PasswordPepper:{version}"] ?? string.Empty;
        return Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC))));
    }

    /// <summary>
    /// Parses the PHC string written by <see cref="HashAsync"/>:
    /// <c>argon2id | v=19 | m=,t=,p= | salt | hash | [p=&lt;pepper version&gt;]</c>.
    /// The Argon parameters are read from the stored string so verification uses
    /// exactly the cost factors the hash was created with; missing or malformed
    /// segments fall back to the configured defaults instead of throwing.
    /// </summary>
    private static ParsedHash? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split('$', StringSplitOptions.RemoveEmptyEntries);

        // argon2id, v=19, params, salt, hash, [pepper]
        if (parts.Length < 5 ||
            !parts[0].Equals("argon2id", StringComparison.Ordinal))
            return null;

        var vString = parts[1].StartsWith("v=", StringComparison.Ordinal)
            ? parts[1][2..]
            : string.Empty;

        if (!int.TryParse(vString, out var version) || version != ArgonVersion)
            return null;

        var hashParams = ParseParams(parts[2])
                     ?? new HashParams(DefaultMemoryKb, DefaultIterations, DefaultParallelism);

        // The trailing p= segment is the pepper version.
        var pepperPart = parts.LastOrDefault(
            x => x.StartsWith("p=", StringComparison.Ordinal));

        var pepper = pepperPart is not null &&
                     int.TryParse(pepperPart[2..], out var pv)
            ? pv
            : 1;

        return new ParsedHash(
            Salt: Convert.FromBase64String(parts[3]),
            Hash: Convert.FromBase64String(parts[4]),
            Pepper: pepper,
            Params: hashParams
        );
    }

    private static HashParams? ParseParams(string segment)
    {
        int len = segment.Length;
        if (len < 3 || segment[0] != 'm' || segment[1] != '=')
            return null;

        int memoryKb = DefaultMemoryKb,
            iterations = DefaultIterations,
            parallelism = DefaultParallelism;

        bool sawMemory = false;
        char key = '\0';
        int valueStart = 0;
        int step = 0; // 0: expect key, 1: expect '=', 2: reading value, -1: invalid chunk

        for (int i = 0; i <= len; i++)
        {
            char c = i < len ? segment[i] : ',';

            if (c == ',')
            {
                if (step == 2 &&
                    int.TryParse(
                        segment.AsSpan(valueStart, i - valueStart),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int val))
                {
                    switch (key)
                    {
                        case 'm':
                            memoryKb = val; 
                            sawMemory = true;
                            break;
                        case 't':
                            iterations = val;
                            break;
                        case 'p':
                            parallelism = val;
                            break;
                    }
                }

                step = 0;
            }
            else if (step == 0)
            {
                key = c;
                step = (c == 'm' || c == 't' || c == 'p') ? 1 : -1;
            }
            else if (step == 1)
            {
                if (c == '=')
                {
                    valueStart = i + 1;
                    step = 2;
                }
                else
                {
                    step = -1;
                }
            }
        }

        if (!sawMemory) return null;

        memoryKb = Clamp(memoryKb, 8192, 1048576);
        iterations = Clamp(iterations, 1, 16);
        parallelism = Clamp(parallelism, 1, 16);

        return new(memoryKb, iterations, parallelism);
    }

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static int GetInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) ? value : fallback;
}
