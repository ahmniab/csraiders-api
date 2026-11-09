using CsRaiders.Api.Application.Features.Auth.Commands.Register;
using CsRaiders.Api.Application.Features.Auth.Commands.ResendOtp;
using CsRaiders.Api.Application.Features.Auth.Commands.VerifyOtp;
using CsRaiders.Api.Application.Features.Auth.Commands.Login;
using CsRaiders.Api.Application.Common.Abstractions;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CsRaiders.Api.API.Services;

namespace CsRaiders.Api.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
public sealed class AuthController(IMediator mediator, ISessionCookieWriter cookies) : ControllerBase
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

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new LoginCommand(
            request.Email, request.Password, HttpContext.Connection.RemoteIpAddress,
            cookies.GetOrCreateDeviceFingerprint(Request.Cookies[SessionCookieWriter.DeviceCookie]), Request.Headers.UserAgent.ToString()), ct);

        if (result.CapacityExhausted)
            return ProblemResult(503, "capacity_exhausted", "The service is temporarily unable to process this request.");
        if (!result.Succeeded)
        {
            if (result.RetryAfter is { } retry && retry > TimeSpan.Zero)
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString();
            return ProblemResult(401, "invalid_credentials", "The email or password is incorrect.");
        }

        foreach (var cookie in cookies.CreateLoginCookies(result.RefreshToken!, result.DeviceFingerprint!,
                     !Request.Cookies.ContainsKey(SessionCookieWriter.DeviceCookie)))
            Response.Cookies.Append(cookie.Name, cookie.Value, new CookieOptions
            {
                Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict,
                Path = "/", MaxAge = cookie.MaxAge, IsEssential = true
            });
        return Ok(new { accessToken = result.AccessToken, sessionId = result.SessionId });
    }

    private AcceptedResult AcceptedResult() => base.Accepted(value: AcceptedPayload);

    private ObjectResult ProblemResult(int status, string code, string message) =>
        StatusCode(status, new { error = new { code, message } });
}

public sealed record RegisterRequest(string Email, string Password, string? Name);
public sealed record VerifyOtpRequest(string Email, string Code);
public sealed record ResendOtpRequest(string Email);
public sealed record LoginRequest(string Email, string Password);
