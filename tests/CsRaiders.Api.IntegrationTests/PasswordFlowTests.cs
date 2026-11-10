using System.Reflection;
using CsRaiders.Api.API.Controllers;
using CsRaiders.Api.Application.Features.Auth.Commands.ChangePassword;
using CsRaiders.Api.Application.Features.Auth.Commands.ForgotPassword;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.IntegrationTests;

public sealed class PasswordFlowTests
{
    [Fact]
    public void Password_routes_have_the_expected_authentication_contract()
    {
        var controller = typeof(AuthController);
        Assert.NotNull(controller.GetMethod(nameof(AuthController.ForgotPassword))!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(controller.GetMethod(nameof(AuthController.ResetPassword))!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(controller.GetMethod(nameof(AuthController.ChangePassword))!.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("forgot-password", controller.GetMethod(nameof(AuthController.ForgotPassword))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("reset-password", controller.GetMethod(nameof(AuthController.ResetPassword))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("change-password", controller.GetMethod(nameof(AuthController.ChangePassword))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Password_commands_expose_single_use_reset_and_current_session_change_inputs()
    {
        Assert.Equal(new[] { "Token", "Password" }, typeof(ResetPasswordCommand).GetProperties().Select(p => p.Name));
        Assert.Equal(new[] { "UserId", "CurrentSessionId", "CurrentPassword", "NewPassword" },
            typeof(ChangePasswordCommand).GetProperties().Select(p => p.Name));
        Assert.Equal(SessionRevokeReason.PasswordChanged, Enum.Parse<SessionRevokeReason>("PasswordChanged"));
    }

    [Fact]
    public void Reset_tokens_are_single_use_and_new_requests_can_invalidate_old_tokens()
    {
        var now = DateTimeOffset.UtcNow;
        var token = PasswordResetToken.Issue(Guid.NewGuid(), new byte[32], null, now);
        Assert.True(token.TryConsume(now));
        Assert.False(token.TryConsume(now.AddSeconds(1)));

        var newer = PasswordResetToken.Issue(Guid.NewGuid(), new byte[32], null, now);
        newer.Invalidate(now);
        Assert.False(newer.IsUsable(now));
    }
}
