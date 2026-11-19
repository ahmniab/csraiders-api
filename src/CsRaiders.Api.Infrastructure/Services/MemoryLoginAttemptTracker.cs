
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;
using CsRaiders.Api.Application.Factories;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class MemoryLoginAttemptTracker(
    IDistributedCache cache) : ILoginAttemptTracker
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = AuthRules.LoginFailureWindow
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<LoginGate> EvaluateAsync(
        Email email,
        IPAddress? ip,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var attempt = await GetAttemptAsync(email, ip, ct);

        return attempt?.Evaluate(now) ?? LoginGate.Open;
    }

    public async Task<LoginFailureOutcome> RegisterFailureAsync(
        Email email,
        IPAddress? ip,
        Guid? userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var attempt = await GetAttemptAsync(email, ip, ct);

        attempt ??= LoginAttemptFactory.For(email, NormalizeIp(ip), now);

        var outcome = attempt.RegisterFailure(now, NormalizeIp(ip), userId);

        await SaveAttemptAsync(email, ip, attempt, ct);

        return outcome;
    }

    public async Task ResetAsync(
        Email email,
        IPAddress? ip,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var attempt = await GetAttemptAsync(email, ip, ct);

        if (attempt is null)
            return;

        attempt.Reset(now);

        await SaveAttemptAsync(email, ip, attempt, ct);
    }

    private async Task<LoginAttempt?> GetAttemptAsync(
        Email email,
        IPAddress? ip,
        CancellationToken ct)
    => await cache.GetObjectAsync<LoginAttempt>(CreateKey(email, ip), ct: ct);

    private async Task SaveAttemptAsync(
        Email email,
        IPAddress? ip,
        LoginAttempt attempt,
        CancellationToken ct)
        => await cache.SetObjectAsync(
            CreateKey(email, ip), 
            attempt,                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        
            CacheOptions, 
            ct);


    private static string CreateKey(Email email, IPAddress? ip)
        => $"login-attempt:{email}:{NormalizeIp(ip)}";
    

    private static IPAddress NormalizeIp(IPAddress? ip)
        => ip ?? IPAddress.Parse("0.0.0.0");
}
