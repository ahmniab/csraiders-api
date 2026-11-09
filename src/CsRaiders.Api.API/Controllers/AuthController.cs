using CsRaiders.Api.Application.Features.Auth.Commands.Register;
using CsRaiders.Api.Application.Features.Auth.Commands.ResendOtp;
using CsRaiders.Api.Application.Features.Auth.Commands.VerifyOtp;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    private static readonly object AcceptedPayload = new { message = "If the account can be registered, a verification message will be sent." };

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new RegisterCommand(request.Email, request.Password, request.Name), ct);
        return result.AlreadyRegistered
            ? ProblemResult(409, "already_registered", "An account is already registered for this email address.")
            : result.CapacityExhausted
            ? ProblemResult(503, "capacity_exhausted", "The service is temporarily unable to process this request.")
            : AcceptedResult();
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(VerifyOtpRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new VerifyOtpCommand(request.Email, request.Code), ct);
        return result.Verified
            ? Ok(new { message = "Email verified." })
            : ProblemResult(400, "invalid_or_expired_otp", "The verification code is invalid or expired.");
    }

    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp(ResendOtpRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new ResendOtpCommand(request.Email), ct);
        return result.CapacityExhausted
            ? ProblemResult(503, "capacity_exhausted", "The service is temporarily unable to process this request.")
            : AcceptedResult();
    }

    private AcceptedResult AcceptedResult() => base.Accepted(value: AcceptedPayload);

    private ObjectResult ProblemResult(int status, string code, string message) =>
        StatusCode(status, new { error = new { code, message } });
}

public sealed record RegisterRequest(string Email, string Password, string? Name);
public sealed record VerifyOtpRequest(string Email, string Code);
public sealed record ResendOtpRequest(string Email);
