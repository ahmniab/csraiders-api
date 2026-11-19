namespace CsRaiders.Api.Application.Common.Abstractions;

/// <summary>
/// Collects per-step timings for a single request pipeline and emits a summary
/// when the outermost step completes. Registered as a scoped service so all
/// steps of one request (HTTP middleware and Mediator pipeline behaviors)
/// share the same measurement tree.
/// </summary>
public interface IRequestStepTimer
{
    /// <summary>
    /// Starts a named timing step; dispose the returned scope to stop it.
    /// Steps nest naturally (middleware wrapping downstream middleware,
    /// pipeline behaviors wrapping handlers) and are rendered as a tree in
    /// the "Request pipeline timing" summary log entry.
    /// </summary>
    IDisposable Track(string stepName);

    /// <summary>Attaches request metadata shown in the pipeline summary. The first call wins.</summary>
    void SetRequestContext(string method, string path);

    /// <summary>Attaches the final response status code shown in the pipeline summary. The last call wins.</summary>
    void MarkResponse(int statusCode);
}
