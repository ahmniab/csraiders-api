using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CsRaiders.Api.API.Filters;

public sealed class OriginAndHeaderValidationFilter(IConfiguration configuration) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = configuration["Site:Origin"];
        var origin = context.HttpContext.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin) && Uri.TryCreate(context.HttpContext.Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer))
            origin = referer.GetLeftPart(UriPartial.Authority);

        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.HttpContext.Request.Headers["X-Requested-With"], "csraiders", StringComparison.Ordinal) ||
            !(context.HttpContext.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            context.Result = new ObjectResult(new { error = new { code = "forbidden", message = "The request headers are not valid." } })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
