using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Application.Extensions;
using System.Net;
using System.Security.Cryptography;
using CsRaiders.Api.Application.Common.EmailTemplates.Models;
using CsRaiders.Api.Application.Common.EmailTemplates.Templates;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Features.Auth.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email, IPAddress? Ip) : IRequest<ForgotPasswordResult>;

public sealed record ForgotPasswordResult(bool Accepted = true, bool CapacityExhausted = false)
{
    public static ForgotPasswordResult Capacity() => new(false, true);
}

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public sealed class ForgotPasswordCommandHandler(
    IUserRepository users,
    IPasswordResetTokenRepository resetTokens,
    ITemplatedEmailQueue emailQueue,
    ISecurityAuditWriter audit,
    IPlatformClock clock,
    PasswordResetTemplate? resetTemplate = null) : IRequestHandler<ForgotPasswordCommand, ForgotPasswordResult>
{
    private readonly PasswordResetTemplate _resetTemplate = resetTemplate ?? new();

    public async ValueTask<ForgotPasswordResult> Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        var email = Email.Create(request.Email);
        var now = clock.UtcNow;
        var user = await users.FindByEmailAsync(email, ct: ct);
        if (user is null || !user.CanAuthenticate()) return new();
        if (await resetTokens.CountIssuedSinceAsync(user.Id, now - AuthRules.OtpSendWindow, ct) >= AuthRules.OtpMaxSendsPerHour)
            return new();

        await resetTokens.InvalidateAllByUserIdAsync(user.Id, now, ct);
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var token = PasswordResetTokenFactory.Issue(user.Id, SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)), request.Ip, now);
        await resetTokens.AddAsync(token, ct);
        await audit.WriteAsync("password_reset_issued", user.Id, email, request.Ip, ct: ct);

        var resetUrl = $"/reset-password?token={rawToken}";
        return emailQueue.TryEnqueueTemplate(
            email.Value,
            _resetTemplate,
            new PasswordResetTemplateModel(resetUrl, user.Name))
            ? new()
            : ForgotPasswordResult.Capacity();
    }
}

public sealed record ResetPasswordCommand(string Token, string Password) : IRequest<ResetPasswordResult>;

public sealed record ResetPasswordResult(bool Succeeded, string? ErrorCode = null)
{
    public static ResetPasswordResult Invalid() => new(false, "invalid_or_expired_reset_token");
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).NotNull().Must(p =>
            p.Normalize(System.Text.NormalizationForm.FormC).Length is >= AuthRules.PasswordMinLength and <= AuthRules.PasswordMaxLength)
            .WithMessage("Password must be between 8 and 128 characters.");
    }
}

public sealed class ResetPasswordCommandHandler(
    IPasswordResetTokenRepository resetTokens,
    IUserRepository users,
    IPasswordHasher passwords,
    ISessionRepository sessions,
    ILoginAttemptRepository loginAttempts,
    IDistributedCache cache,
    ISecurityAuditWriter audit,
    IPlatformClock clock) : IRequestHandler<ResetPasswordCommand, ResetPasswordResult>
{
    public async ValueTask<ResetPasswordResult> Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        
        var candidate 
            = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Token));
        
        var token =
            await resetTokens.FindByTokenHashAsync(candidate, track: true, ct);
        var sameToken = CryptographicOperations.FixedTimeEquals(token.TokenHash, candidate);
        if (token == null || !sameToken || !token.IsUsable(now))
            return ResetPasswordResult.Invalid();

        var user = 
            await users.FindByIdAsync(token.UserId, track: true, ct);
        
        if (user is null || !user.CanAuthenticate() || !token.TryConsume(now)) 
            return ResetPasswordResult.Invalid();
        
        var hash = await passwords.HashAsync(request.Password, ct);
        
        if (hash.Outcome == HashOutcome.Unavailable) 
            return ResetPasswordResult.Invalid();

        user.ChangePassword(hash.Hash!, now);
        users.Update(user);
        
        var active = 
            await sessions.FindActiveByUserIdAsync(user.Id, now, track: false, ct);
        
        await sessions.RevokeAllByUserIdAsync(user.Id, SessionRevokeReason.PasswordReset, now, ct);
        
        foreach (var session in active) 
            cache.RemoveAsync($"sid:{session.Id}");
        
        await loginAttempts.ResetByUserIdAsync(user.Id, now, ct);
        await audit.WriteAsync("password_reset_completed", user.Id, user.Email, ct: ct);
        return new(true);
    }
}
