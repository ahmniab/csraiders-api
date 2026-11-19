using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.API.Controllers;

/// <summary>
/// Minimal liveness probe. Not rate-limited, not authenticated.
/// Returns 200 as soon as Kestrel is serving — no DB round-trip so it
/// isolates the pure framework/network ceiling during benchmarks and is
/// safe to use as a compose healthcheck.
/// </summary>
[ApiController]
[Route("health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private static readonly object Payload = new { status = "ok" };

    [HttpGet]
    public IActionResult Get() => Ok(Payload);
}
