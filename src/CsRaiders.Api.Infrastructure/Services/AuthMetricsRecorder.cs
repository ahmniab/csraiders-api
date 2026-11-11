using System.Collections.Concurrent;
using CsRaiders.Api.Application.Common.Interfaces.Security;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class AuthMetricsRecorder : IAuthMetricsRecorder
{
    private readonly ConcurrentDictionary<string, long> counters = new(StringComparer.Ordinal);
    public void Increment(string counter) => counters.AddOrUpdate(counter, 1, (_, current) => current + 1);
    public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(counters, StringComparer.Ordinal);
}
