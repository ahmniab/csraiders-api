using System.Security.Claims;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Features.Sessions.Queries;
using Mediator;

namespace CsRaiders.Api.API.Middleware;

public sealed class SessionValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, 
        IMediator mediator)
    {
        var sidClaim = 
            context.User.FindFirstValue("sid");
        
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(sidClaim, out var sid))
        {
            await next(context);
            return;
        }

        var status = await mediator.Send(new GetSessionStatusByIdQuery(sid.ToString()));
        
        if (status.IsActive())
            await next(context);
        
        context.Response.Cookies.Delete("__Host-csraiders_rt");
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "session_superseded", 
                message = "The session is no longer active."
            }
        });
    }
}
