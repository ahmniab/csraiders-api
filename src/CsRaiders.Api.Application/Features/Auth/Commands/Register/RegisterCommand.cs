using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using FluentValidation;
using MediatR;

namespace CsRaiders.Api.Application.Features.Auth.Commands.Register;

public sealed record RegisterCommand(string Email, string Password, string? Name) : IRequest<RegisterResult>;

public sealed record RegisterResult(bool Accepted, bool CapacityExhausted = false)
{
    public static RegisterResult Generic() => new(true);
    public static RegisterResult Capacity() => new(false, true);
}

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotNull().Must(p =>
        {
            var normalized = p.Normalize(System.Text.NormalizationForm.FormC);
            return normalized.Length is >= 8 and <= 128;
        }).WithMessage("Password must be between 8 and 128 characters.");
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
    }
}

public sealed class RegisterCommandHandler(
    IUserRepository users,
    IPendingRegistrationRepository pendingRegistrations,
    IPasswordHasher passwords,
    IOtpGenerator otps,
    IEmailQueue emailQueue,
    IPlatformClock clock) : IRequestHandler<RegisterCommand, RegisterResult>
{
    public async Task<RegisterResult> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = Email.Create(request.Email);
        var now = clock.UtcNow;
        var displayName = string.IsNullOrWhiteSpace(request.Name) ? email.Value.Split('@')[0] : request.Name!;
        var existing = await users.FindByEmailAsync(email, ct: ct);

        if (existing is not null)
        {
            return emailQueue.TryEnqueue(new EmailMessage(email.Value, "CS Raiders account notice",
                "An account already exists for this email address.", EmailPurpose.AccountNotice))
                ? RegisterResult.Generic() : RegisterResult.Capacity();
        }

        var staged = await pendingRegistrations.FindByEmailAsync(email, track: true, ct);
        var otp = otps.Generate();
        var otpHash = otps.Protect(email.Value, otp);

        if (staged is not null)
        {
            var result = staged.Reissue(displayName, otpHash, now);
            if (result != Domain.Enums.OtpSendResult.Sent) return RegisterResult.Generic();
            return EnqueueRegistration(email, otp) ? RegisterResult.Generic() : RegisterResult.Capacity();
        }

        var password = await passwords.HashAsync(request.Password, ct);
        if (password.Outcome == HashOutcome.Unavailable) return RegisterResult.Capacity();

        var registration = PendingRegistration.Start(email, displayName, password.Hash!, otpHash, null, now);
        await pendingRegistrations.AddAsync(registration, ct);
        return EnqueueRegistration(email, otp) ? RegisterResult.Generic() : RegisterResult.Capacity();
    }

    private bool EnqueueRegistration(Email email, string otp) =>
        emailQueue.TryEnqueue(new EmailMessage(email.Value, "Verify your CS Raiders account",
            $"Your verification code is {otp}.", EmailPurpose.RegistrationOtp));
}
