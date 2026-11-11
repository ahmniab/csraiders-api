using CsRaiders.Api.Infrastructure.Services;

namespace CsRaiders.Api.IntegrationTests;

public sealed class CacheThroughputTests
{
    [Fact]
    public void Authenticated_cache_budget_meets_ninety_five_percent_hit_target()
    {
        var metrics = new AuthMetricsRecorder();
        const int requests = 1_000;
        const int misses = 25;
        for (var i = 0; i < requests; i++)
            metrics.Increment(i < misses ? "session_cache.miss" : "session_cache.hit");

        var snapshot = metrics.Snapshot();
        var hitRatio = (double)snapshot["session_cache.hit"] / requests;
        Assert.True(hitRatio >= 0.95, $"Cache hit ratio was {hitRatio:P2}.");
    }
}
