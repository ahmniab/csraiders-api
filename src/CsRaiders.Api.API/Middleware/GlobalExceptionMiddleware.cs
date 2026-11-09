using System.Net;
using System.Text.Json;
using FluentValidation;
using CsRaiders.Api.Domain.Exceptions;

namespace CsRaiders.Api.API.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = ex switch
        {
            ValidationException => StatusCodes.Status422UnprocessableEntity,
            DomainException => StatusCodes.Status422UnprocessableEntity,
            _ => (int)HttpStatusCode.InternalServerError
        };

        var code = ex switch
        {
            ValidationException => "validation_failed",
            DomainException domain => domain.Code,
            _ => "internal_error"
        };
        var result = JsonSerializer.Serialize(new { error = new { code, message = ex.Message } });
        return context.Response.WriteAsync(result);
    }
}
