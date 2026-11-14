using CsRaiders.Api.Application.Common.EmailTemplates.Models;
using CsRaiders.Api.Application.Common.EmailTemplates.Templates;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using FluentValidation;
using Mediator;

namespace CsRaiders.Api.Application.Features.Auth.Commands.ResendOtp;

public sealed record ResendOtpCommand(string Email) : IRequest<ResendOtpResult>;
public sealed record ResendOtpResult(bool Accepted, bool CapacityExhausted = false)
{
    public static ResendOtpResult Generic() => new(true);
    public static ResendOtpResult Capacity() => new(false, true);
}

public sealed class ResendOtpCommandValidator : AbstractValidator<ResendOtpCommand>
{
    public ResendOtpCommandValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public sealed class ResendOtpCommandHandler(
    IUserRepository users,
    IPendingRegistrationRepository pendingRegistrations,
    IOtpGenerator otps,
    ITemplatedEmailQueue emailQueue,
    IPlatformClock clock,
    RegistrationOtpTemplate? otpTemplate = null,
    SecurityNoticeTemplate? noticeTemplate = null) : IRequestHandler<ResendOtpCommand, ResendOtpResult>
{
    private readonly RegistrationOtpTemplate _otpTemplate = otpTemplate ?? new();
    private readonly SecurityNoticeTemplate _noticeTemplate = noticeTemplate ?? new();

    public async ValueTask<ResendOtpResult> Handle(ResendOtpCommand request, CancellationToken ct)
    {
        var email = Email.Create(request.Email);
        var existing = await users.FindByEmailAsync(email, ct: ct);
        if (existing is not null)
            return emailQueue.TryEnqueueTemplate(
                email.Value,
                _noticeTemplate,
                new SecurityNoticeTemplateModel("Account Notice", "An account already exists for this email address.", existing.Name, clock.UtcNow))
                ? ResendOtpResult.Generic() : ResendOtpResult.Capacity();

        var staged = await pendingRegistrations.FindByEmailAsync(email, track: true, ct);
        if (staged is null) return ResendOtpResult.Generic();

        var otp = otps.Generate();
        var result = staged.Reissue(staged.Name, otps.Protect(email.Value, otp), clock.UtcNow);
        if (result != Domain.Enums.OtpSendResult.Sent) return ResendOtpResult.Generic();
        return emailQueue.TryEnqueueTemplate(
            email.Value,
            _otpTemplate,
            new RegistrationOtpTemplateModel(otp, staged.Name))
            ? ResendOtpResult.Generic() : ResendOtpResult.Capacity();
    }
}
