using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class AuthCleanupWorker(IServiceScopeFactory scopes, ILogger<AuthCleanupWorker> logger) : BackgroundService
{
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCleanupAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Authentication cleanup pass failed"); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    public async Task<CleanupCounts> RunCleanupAsync(CancellationToken ct = default)
    {
        if (!await RunGate.WaitAsync(0, ct)) return new CleanupCounts();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await using var scope = scopes.CreateAsyncScope();
            var sessions = await scope.ServiceProvider.GetRequiredService<ISessionRepository>().DeleteStaleAsync(now, now.AddDays(-30), ct);
            var pending = await scope.ServiceProvider.GetRequiredService<IPendingRegistrationRepository>().DeleteExpiredAsync(now, ct);
            var resets = await scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>().DeleteStaleAsync(now.AddDays(-7), ct);
            var attempts = await scope.ServiceProvider.GetRequiredService<ILoginAttemptRepository>().DeleteStaleAsync(now.AddHours(-24), ct);
            var events = await scope.ServiceProvider.GetRequiredService<ISecurityEventRepository>().PurgeBeforeAsync(now.AddDays(-30), ct);
            var counts = new CleanupCounts(sessions, pending, resets, attempts, events);
            logger.LogInformation("Authentication cleanup complete: {@Counts}", counts);
            return counts;
        }
        finally { RunGate.Release(); }
    }
}

public sealed record CleanupCounts(int Sessions = 0, int PendingRegistrations = 0, int ResetTokens = 0, int LoginAttempts = 0, int SecurityEvents = 0);
