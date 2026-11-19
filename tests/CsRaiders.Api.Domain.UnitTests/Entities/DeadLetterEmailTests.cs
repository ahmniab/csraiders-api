using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Domain.Entities;
using FluentAssertions;

namespace CsRaiders.Api.Domain.UnitTests.Entities;

public class DeadLetterEmailTests
{
    [Fact]
    public void Create_initializes_all_properties_correctly()
    {
        var failedAt = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        var deadLetter = DeadLetterEmailFactory.Create(
            "student@example.com",
            "Verify your account",
            "registration_otp",
            "Connection refused (SMTP server unreachable)",
            3,
            "corr-456",
            failedAt);

        deadLetter.Id.Should().NotBeEmpty();
        deadLetter.Recipient.Should().Be("student@example.com");
        deadLetter.Subject.Should().Be("Verify your account");
        deadLetter.Purpose.Should().Be("registration_otp");
        deadLetter.ErrorReason.Should().Be("Connection refused (SMTP server unreachable)");
        deadLetter.AttemptCount.Should().Be(3);
        deadLetter.CorrelationId.Should().Be("corr-456");
        deadLetter.FailedAt.Should().Be(failedAt);
    }

    [Fact]
    public void Create_without_correlation_id_generates_id()
    {
        var failedAt = DateTimeOffset.UtcNow;
        var deadLetter = DeadLetterEmailFactory.Create(
            "student@example.com",
            "Verify your account",
            "registration_otp",
            "Timeout",
            3,
            null,
            failedAt);

        deadLetter.Id.Should().NotBeEmpty();
        deadLetter.CorrelationId.Should().BeNull();
    }
}
