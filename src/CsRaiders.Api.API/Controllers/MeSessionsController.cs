using System.Security.Claims;
using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.Application.Features.Sessions.Commands;
using CsRaiders.Api.Application.Features.Sessions.Queries;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.API.Controllers;

[ApiController]
[Authorize(Policy = AuthPolicies.AuthenticatedOnly)]
[Route("api/v1/me/sessions")]
public sealed class MeSessionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = UserId();
        var currentSessionId = SessionId();
        var sessions = await mediator.Send(new GetSessionSummariesQuery(userId), ct);
        return Ok(new
        {
            currentSessionId,
            sessions = sessions.Select(s => new
            {
                sessionId = s.Id,
                deviceLabel = s.DeviceLabel,
                ip = s.Ip?.ToString(),
                userAgent = s.UserAgent,
                createdAt = s.CreatedAt,
                lastSeenAt = s.LastSeenAt
            })
        });
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Revoke(Guid sessionId, CancellationToken ct)
    {
        var result = await mediator.Send(new RevokeSessionCommand(UserId(), sessionId), ct);
        return result.Revoked ? Ok(new { revoked = true }) : NotFound();
    }

    [HttpPost("revoke-others")]
    public async Task<IActionResult> RevokeOthers(CancellationToken ct)
    {
        var result = await mediator.Send(new RevokeAllOtherSessionsCommand(UserId(), SessionId()), ct);
        return Ok(new { revokedCount = result.RevokedCount });
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue("sub")!);
    private Guid SessionId() => Guid.Parse(User.FindFirstValue("sid")!);
}
