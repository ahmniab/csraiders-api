using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using FluentValidation;
using Mediator;

namespace CsRaiders.Api.Application.Features.Auth.Commands.VerifyOtp;

public sealed record VerifyOtpCommand(string Email, string Code) : IRequest<VerifyOtpResult>;
public sealed record VerifyOtpResult(bool Verified, bool InvalidOrExpired = false)
{
    public static VerifyOtpResult Success() => new(true);
    public static VerifyOtpResult Failure() => new(false, true);
}

public sealed class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).Matches("^[0-9]{6}$");
    }
}

public sealed class VerifyOtpCommandHandler(
    IPendingRegistrationRepository pendingRegistrations,
    IUserRepository users,
    IRoleRepository roles,
    IOtpGenerator otps,
    ISecurityAuditWriter audit,
    IPlatformClock clock) : IRequestHandler<VerifyOtpCommand, VerifyOtpResult>
{
    public async ValueTask<VerifyOtpResult> Handle(VerifyOtpCommand request, CancellationToken ct)
    {
        var email = Email.Create(request.Email);
        var pending = await pendingRegistrations.FindByEmailAsync(email, track: true, ct);
        if (pending is null) return VerifyOtpResult.Failure();

        var outcome = pending.Verify(otps.Protect(email.Value, request.Code), clock.UtcNow);
        if (outcome != Domain.Enums.OtpVerifyResult.Verified)
        {
            await audit.WriteAsync("registration_otp_failed", email: email,
                details: new { outcome = outcome.ToString() }, ct: ct);
            return VerifyOtpResult.Failure();
        }

        var user = User.RegisterVerified(email, pending.Name, pending.PasswordHash, clock.UtcNow);
        var student = await roles.FindBySlugAsync("student", ct: ct)
            ?? throw new InvalidOperationException("The system student role is not seeded.");
        user.AssignRole(student.Id, null, clock.UtcNow);
        await users.AddAsync(user, ct);
        await pendingRegistrations.DeleteByEmailAsync(email, ct);
        return VerifyOtpResult.Success();
    }
}
