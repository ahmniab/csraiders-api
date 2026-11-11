using CsRaiders.Api.Infrastructure.Services;

namespace CsRaiders.Api.IntegrationTests;

public sealed class ObservabilityTests
{
    [Fact]
    public void Auth_metrics_recorder_exposes_named_counters()
    {
        var metrics = new AuthMetricsRecorder();
        metrics.Increment("session_cache.hit");
        metrics.Increment("session_cache.hit");
        metrics.Increment("permission_cache.miss");

        var snapshot = metrics.Snapshot();
        Assert.Equal(2, snapshot["session_cache.hit"]);
        Assert.Equal(1, snapshot["permission_cache.miss"]);
    }
}
