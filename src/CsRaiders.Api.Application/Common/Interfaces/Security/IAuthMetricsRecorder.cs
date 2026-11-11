namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface IAuthMetricsRecorder
{
    void Increment(string counter);
    IReadOnlyDictionary<string, long> Snapshot();
}
