using System.Text.RegularExpressions;
using CsRaiders.Api.Application.Common.EmailTemplates.Models;
using CsRaiders.Api.Application.Common.EmailTemplates.Templates;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Exceptions;
using CsRaiders.Api.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace CsRaiders.Api.Application.UnitTests.EmailTemplates;

public class EmailTemplateRenderingTests
{
    private readonly EmailTemplateService _templateService = new();
    private readonly RegistrationOtpTemplate _otpTemplate = new();
    private readonly PasswordResetTemplate _resetTemplate = new();
    private readonly SecurityNoticeTemplate _noticeTemplate = new();

    [Fact]
    public void RegistrationOtpTemplate_RendersExpectedContent()
    {
        var model = new RegistrationOtpTemplateModel("482910", "Alice", 15);

        var subject = _otpTemplate.BuildSubject(model);
        var html = _otpTemplate.RenderHtml(model);
        var text = _otpTemplate.RenderText(model);

        _otpTemplate.Purpose.Should().Be(EmailPurpose.RegistrationOtp);
        subject.Should().Be("Verify your CS Raiders account");

        // HTML checks
        html.Should().Contain("CS Raiders");
        html.Should().Contain("Hi Alice,");
        html.Should().Contain("482910");
        html.Should().Contain("15 minutes");
        html.Should().Contain("<!DOCTYPE html>");

        // Text checks
        text.Should().Contain("Hi Alice,");
        text.Should().Contain("482910");
        text.Should().Contain("15 minutes");
        text.Should().NotContain("<p>");
        text.Should().NotContain("</div>");
    }

    [Fact]
    public void PasswordResetTemplate_RendersExpectedContent()
    {
        var model = new PasswordResetTemplateModel("https://csraiders.com/reset?token=test-token-123", "Bob", 60);

        var subject = _resetTemplate.BuildSubject(model);
        var html = _resetTemplate.RenderHtml(model);
        var text = _resetTemplate.RenderText(model);

        _resetTemplate.Purpose.Should().Be(EmailPurpose.PasswordReset);
        subject.Should().Be("Reset your CS Raiders password");

        // HTML checks
        html.Should().Contain("CS Raiders");
        html.Should().Contain("Hi Bob,");
        html.Should().Contain("https://csraiders.com/reset?token=test-token-123");
        html.Should().Contain("Reset Password");
        html.Should().Contain("60 minutes");
        html.Should().Contain("<!DOCTYPE html>");

        // Text checks
        text.Should().Contain("Hi Bob,");
        text.Should().Contain("https://csraiders.com/reset?token=test-token-123");
        text.Should().Contain("60 minutes");
        text.Should().NotContain("<a href=");
        text.Should().NotContain("<p>");
    }

    [Fact]
    public void SecurityNoticeTemplate_RendersExpectedContent()
    {
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        var model = new SecurityNoticeTemplateModel("New Login Detected", "Login from Chrome on Linux.", "Charlie", now);

        var subject = _noticeTemplate.BuildSubject(model);
        var html = _noticeTemplate.RenderHtml(model);
        var text = _noticeTemplate.RenderText(model);

        _noticeTemplate.Purpose.Should().Be(EmailPurpose.AccountNotice);
        subject.Should().Be("Security Alert: New Login Detected");

        // HTML checks
        html.Should().Contain("CS Raiders");
        html.Should().Contain("Hi Charlie,");
        html.Should().Contain("New Login Detected");
        html.Should().Contain("Login from Chrome on Linux.");
        html.Should().Contain("2026-11-14 12:00:00Z");
        html.Should().Contain("<!DOCTYPE html>");

        // Text checks
        text.Should().Contain("Hi Charlie,");
        text.Should().Contain("Security Alert: New Login Detected");
        text.Should().Contain("Login from Chrome on Linux.");
        text.Should().Contain("2026-11-14 12:00:00Z");
        text.Should().NotContain("<h3");
        text.Should().NotContain("<p>");
    }

    [Fact]
    public void EmailTemplateService_Render_ReturnsComposedEmailContent()
    {
        var model = new RegistrationOtpTemplateModel("999888", "David");

        var content = _templateService.Render(_otpTemplate, model);

        content.Should().NotBeNull();
        content.Subject.Should().Be("Verify your CS Raiders account");
        content.BodyHtml.Should().Contain("999888");
        content.BodyText.Should().Contain("999888");
    }

    [Fact]
    public void EmailTemplateService_CreateMessage_ProducesValidEmailMessage()
    {
        var model = new PasswordResetTemplateModel("https://csraiders.com/reset?token=abc", "Eve");

        var message = _templateService.CreateMessage(
            "  Eve@Example.Com  ",
            _resetTemplate,
            model,
            "corr-12345");

        message.Should().NotBeNull();
        // Recipient must be normalized (trimmed and lowercased)
        message.To.Should().Be("eve@example.com");
        message.Subject.Should().Be("Reset your CS Raiders password");
        message.Purpose.Should().Be(EmailPurpose.PasswordReset);
        message.CorrelationId.Should().Be("corr-12345");
        message.BodyHtml.Should().Contain("Reset Password");
        message.BodyText.Should().Contain("https://csraiders.com/reset?token=abc");
        message.Body.Should().Be(message.BodyHtml);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmailTemplateService_CreateMessage_Throws_WhenRecipientEmpty(string? invalidRecipient)
    {
        var model = new RegistrationOtpTemplateModel("123456", "Test");

        var act = () => _templateService.CreateMessage(invalidRecipient!, _otpTemplate, model);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@missingusername.com")]
    [InlineData("missing.domain@")]
    public void EmailTemplateService_CreateMessage_Throws_WhenRecipientInvalidSyntax(string invalidEmail)
    {
        var model = new RegistrationOtpTemplateModel("123456", "Test");

        var act = () => _templateService.CreateMessage(invalidEmail, _otpTemplate, model);

        act.Should().Throw<DomainException>()
            .WithMessage("*not valid*");
    }

    [Fact]
    public void EmailTemplateService_Throws_WhenTemplateOrModelIsNull()
    {
        var model = new RegistrationOtpTemplateModel("123456", "Test");

        var actNullTemplate = () => _templateService.Render<RegistrationOtpTemplateModel>(null!, model);
        var actNullModel = () => _templateService.Render(_otpTemplate, null!);

        actNullTemplate.Should().Throw<ArgumentNullException>();
        actNullModel.Should().Throw<ArgumentNullException>();

        var actMsgNullTemplate = () => _templateService.CreateMessage<RegistrationOtpTemplateModel>("user@test.com", null!, model);
        var actMsgNullModel = () => _templateService.CreateMessage("user@test.com", _otpTemplate, null!);

        actMsgNullTemplate.Should().Throw<ArgumentNullException>();
        actMsgNullModel.Should().Throw<ArgumentNullException>();
    }
}
