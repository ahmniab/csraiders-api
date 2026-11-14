using CsRaiders.Api.Domain.Entities;
using FluentAssertions;

namespace CsRaiders.Api.Domain.UnitTests.Entities;

public class EmailMessageTests
{
    [Fact]
    public void Constructor_with_html_and_text_initializes_all_properties()
    {
        var message = new EmailMessage(
            "student@example.com",
            "Verify your account",
            "<h1>Verify</h1><p>Code: 123456</p>",
            "Verify: Code: 123456",
            EmailPurpose.RegistrationOtp,
            "corr-123");

        message.To.Should().Be("student@example.com");
        message.Subject.Should().Be("Verify your account");
        message.BodyHtml.Should().Be("<h1>Verify</h1><p>Code: 123456</p>");
        message.BodyText.Should().Be("Verify: Code: 123456");
        message.Purpose.Should().Be(EmailPurpose.RegistrationOtp);
        message.CorrelationId.Should().Be("corr-123");
        message.Body.Should().Be("<h1>Verify</h1><p>Code: 123456</p>");
    }

    [Fact]
    public void Backward_compatible_constructor_sets_both_html_and_text_to_body()
    {
        var message = new EmailMessage(
            "user@example.com",
            "Notice",
            "Plain notification content",
            EmailPurpose.AccountNotice);

        message.To.Should().Be("user@example.com");
        message.Subject.Should().Be("Notice");
        message.BodyHtml.Should().Be("Plain notification content");
        message.BodyText.Should().Be("Plain notification content");
        message.Purpose.Should().Be(EmailPurpose.AccountNotice);
        message.CorrelationId.Should().BeNull();
        message.Body.Should().Be("Plain notification content");
    }

    [Fact]
    public void Record_equality_compares_values_accurately()
    {
        var msg1 = new EmailMessage("a@b.com", "Sub", "<p>hi</p>", "hi", EmailPurpose.PasswordReset, "c1");
        var msg2 = new EmailMessage("a@b.com", "Sub", "<p>hi</p>", "hi", EmailPurpose.PasswordReset, "c1");
        var msg3 = new EmailMessage("a@b.com", "Sub", "<p>diff</p>", "hi", EmailPurpose.PasswordReset, "c1");

        msg1.Should().Be(msg2);
        msg1.Should().NotBe(msg3);
    }
}
