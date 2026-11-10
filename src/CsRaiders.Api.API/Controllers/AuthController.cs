using CsRaiders.Api.Application.Features.Auth.Commands.Register;
using CsRaiders.Api.Application.Features.Auth.Commands.ResendOtp;
using CsRaiders.Api.Application.Features.Auth.Commands.VerifyOtp;
using CsRaiders.Api.Application.Features.Auth.Commands.Login;
using CsRaiders.Api.Application.Features.Auth.Commands.Refresh;
using CsRaiders.Api.Application.Features.Auth.Commands.ForgotPassword;
using CsRaiders.Api.Application.Features.Auth.Commands.ChangePassword;
using CsRaiders.Api.Application.Common.Abstractions;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CsRaiders.Api.API.Services;
using CsRaiders.Api.API.Filters;
using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.Application.Features.Sessions.Commands;
using System.Security.Claims;

namespace CsRaiders.Api.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IMediator mediator, ISessionCookieWriter cookies) : ControllerBase
{
    private static readonly object AcceptedPayload = new { message = "If the account can be registered, a verification message will be sent." };

    [HttpPost("register")]
    [AllowAnonymous]
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
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp(VerifyOtpRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new VerifyOtpCommand(request.Email, request.Code), ct);
        return result.Verified
            ? Ok(new { message = "Email verified." })
            : ProblemResult(400, "invalid_or_expired_otp", "The verification code is invalid or expired.");
    }

    [HttpPost("resend-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendOtp(ResendOtpRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new ResendOtpCommand(request.Email), ct);
        return result.CapacityExhausted
            ? ProblemResult(503, "capacity_exhausted", "The service is temporarily unable to process this request.")
            : AcceptedResult();
    }

    [HttpPost("login")]
    [AllowAnonymous]
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
        return Ok(new
        {
            accessToken = result.AccessToken,
            // refreshToken = result.RefreshToken,
            sessionId = result.SessionId,
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ServiceFilter(typeof(OriginAndHeaderValidationFilter))]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var result = await mediator.Send(new RefreshTokenCommand(
            Request.Cookies[SessionCookieWriter.RefreshCookie],
            Request.Cookies[SessionCookieWriter.DeviceCookie]), ct);

        if (!result.Succeeded)
        {
            ClearAuthCookies();
            var status = result.ErrorCode is "invalid_grant" or "session_superseded" or "session_compromised"
                ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest;
            var message = result.ErrorCode switch
            {
                "session_compromised" => "The session has been compromised.",
                "session_superseded" => "The session is no longer active.",
                _ => "The refresh grant is invalid."
            };
            return ProblemResult(status, result.ErrorCode ?? "invalid_grant", message);
        }

        AppendCookie(cookies.CreateRefreshCookie(result.RefreshToken!));
        return Ok(new { accessToken = result.AccessToken, sessionId = result.SessionId });
    }

    [HttpPost("logout")]
    [Authorize(Policy = AuthPolicies.AuthenticatedOnly)]
    [ServiceFilter(typeof(OriginAndHeaderValidationFilter))]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await mediator.Send(new LogoutCommand(Guid.Parse(User.FindFirstValue("sub")!),
            Guid.Parse(User.FindFirstValue("sid")!)), ct);
        ClearAuthCookies();
        return Ok(new { loggedOut = true });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new ForgotPasswordCommand(request.Email, HttpContext.Connection.RemoteIpAddress), ct);
        return result.CapacityExhausted
            ? ProblemResult(503, "capacity_exhausted", "The service is temporarily unable to process this request.")
            : AcceptedResult();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new ResetPasswordCommand(request.Token, request.Password), ct);
        return result.Succeeded
            ? Ok(new { message = "Password reset." })
            : ProblemResult(400, result.ErrorCode ?? "invalid_or_expired_reset_token", "The reset token is invalid or expired.");
    }

    [HttpPost("change-password")]
    [Authorize(Policy = AuthPolicies.AuthenticatedOnly)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new ChangePasswordCommand(
            Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("sid")!),
            request.CurrentPassword, request.NewPassword), ct);
        if (result.CapacityExhausted)
            return ProblemResult(503, "capacity_exhausted", "The service is temporarily unable to process this request.");
        return result.Succeeded
            ? Ok(new { message = "Password changed." })
            : ProblemResult(401, result.ErrorCode ?? "invalid_credentials", "The current password is incorrect.");
    }

    private void ClearAuthCookies()
    {
        AppendCookie(cookies.CreateClearRefreshCookie());
        AppendCookie(cookies.CreateClearDeviceCookie());
    }

    private void AppendCookie(SessionCookie cookie) => Response.Cookies.Append(cookie.Name, cookie.Value, new CookieOptions
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict,
        Path = "/", MaxAge = cookie.MaxAge, IsEssential = true
    });

    private AcceptedResult AcceptedResult() => base.Accepted(value: AcceptedPayload);

    private ObjectResult ProblemResult(int status, string code, string message) =>
        StatusCode(status, new { error = new { code, message } });
}

public sealed record RegisterRequest(string Email, string Password, string? Name);
public sealed record VerifyOtpRequest(string Email, string Code);
public sealed record ResendOtpRequest(string Email);
public sealed record LoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
