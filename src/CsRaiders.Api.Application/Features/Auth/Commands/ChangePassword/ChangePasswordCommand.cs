using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Features.Auth.Commands.ChangePassword;

public sealed record ChangePasswordCommand(Guid UserId, Guid CurrentSessionId, string CurrentPassword, string NewPassword)
    : IRequest<ChangePasswordResult>;

public sealed record ChangePasswordResult(bool Succeeded, bool CapacityExhausted = false, string? ErrorCode = null)
{
    public static ChangePasswordResult Invalid() => new(false, ErrorCode: "invalid_credentials");
    public static ChangePasswordResult Capacity() => new(false, true, "capacity_exhausted");
}

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotNull().Must(p =>
            p.Normalize(System.Text.NormalizationForm.FormC).Length is >= AuthRules.PasswordMinLength and <= AuthRules.PasswordMaxLength)
            .WithMessage("Password must be between 8 and 128 characters.");
    }
}

public sealed class ChangePasswordCommandHandler(
    IUserRepository users,
    IPasswordHasher passwords,
    ISessionRepository sessions,
    IDistributedCache cache,
    ISecurityAuditWriter audit,
    IPlatformClock clock) : IRequestHandler<ChangePasswordCommand, ChangePasswordResult>
{
    public async ValueTask<ChangePasswordResult> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(request.UserId, track: true, ct);
        
        if (user is null || !user.CanAuthenticate) 
            return ChangePasswordResult.Invalid();
        
        var verification = 
            await passwords.VerifyAsync(request.CurrentPassword, user.PasswordHash, ct);
        
        if (verification.Outcome == HashOutcome.Unavailable) 
            return ChangePasswordResult.Capacity();
        
        if (!verification.IsValid) 
            return ChangePasswordResult.Invalid();
        
        var hash = await passwords.HashAsync(request.NewPassword, ct);
        
        if (hash.Outcome == HashOutcome.Unavailable) 
            return ChangePasswordResult.Capacity();

        var now = clock.UtcNow;
        user.ChangePassword(hash.Hash!, now);
        users.Update(user);
        
        var active = 
            await sessions.FindActiveByUserIdAsync(user.Id, now, track: false, ct);
        
        await sessions.BulkRevokeByUserIdAsync(
            user.Id, 
            SessionRevokeReason.PasswordChanged, 
            request.CurrentSessionId, 
            now, 
            ct);
        
        foreach (var session in active.Where(s => s.Id != request.CurrentSessionId)) 
            await cache.RemoveAsync($"sid:{session.Id}");
        
        await audit.WriteAsync("password_changed", user.Id, user.Email, ct: ct);
        
        return new(true);
    }
}
