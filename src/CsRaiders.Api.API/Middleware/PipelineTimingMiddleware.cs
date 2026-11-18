using CsRaiders.Api.Application.Common.Abstractions;

namespace CsRaiders.Api.API.Middleware;

/// <summary>
/// Request pipeline timing diagnostics. Each <see cref="UseTimedStep"/> checkpoint measures
/// the full duration of the middleware registered immediately after it and contributes one
/// node to the "Request pipeline timing" summary log emitted once per request.
/// </summary>
public static class PipelineTimingMiddleware
{
    /// <summary>
    /// Registers a timing checkpoint wrapping the middleware registered directly after it.
    /// Nest the checkpoints around each pipeline stage to obtain a per-step breakdown.
    /// </summary>
    public static IApplicationBuilder UseTimedStep(this IApplicationBuilder app, string stepName)
    {
        return app.Use(async (context, next) =>
        {
            var timer = context.RequestServices.GetRequiredService<IRequestStepTimer>();
            timer.SetRequestContext(context.Request.Method, context.Request.Path.Value ?? string.Empty);
            using var step = timer.Track(stepName);
            try
            {
                await next(context);
            }
            finally
            {
                timer.MarkResponse(context.Response.StatusCode);
            }
        });
    }
}
