using System.Net;
using CsRaiders.Api.Application.Common.EmailTemplates.Models;
using CsRaiders.Api.Application.Common.EmailTemplates.Templates;
using FluentAssertions;
using Xunit;

namespace CsRaiders.Api.Application.UnitTests.EmailTemplates;

public class EmailTemplateSanitizationTests
{
    private readonly RegistrationOtpTemplate _otpTemplate = new();
    private readonly PasswordResetTemplate _resetTemplate = new();
    private readonly SecurityNoticeTemplate _noticeTemplate = new();

    [Fact]
    public void RegistrationOtpTemplate_Throws_When_ModelIsNull()
    {
        var actSubject = () => _otpTemplate.BuildSubject(null!);
        var actHtml = () => _otpTemplate.RenderHtml(null!);
        var actText = () => _otpTemplate.RenderText(null!);

        actSubject.Should().Throw<ArgumentNullException>();
        actHtml.Should().Throw<ArgumentNullException>();
        actText.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegistrationOtpTemplate_Throws_When_OtpIsNullOrEmpty(string? invalidOtp)
    {
        var model = new RegistrationOtpTemplateModel(invalidOtp!, "John Doe");

        var act = () => _otpTemplate.RenderHtml(model);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*OTP*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-15)]
    public void RegistrationOtpTemplate_Throws_When_ExpiryMinutesIsNotPositive(int invalidExpiry)
    {
        var model = new RegistrationOtpTemplateModel("123456", "John Doe", invalidExpiry);

        var act = () => _otpTemplate.RenderHtml(model);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Expiry*");
    }

    [Fact]
    public void PasswordResetTemplate_Throws_When_ModelIsNull()
    {
        var actSubject = () => _resetTemplate.BuildSubject(null!);
        var actHtml = () => _resetTemplate.RenderHtml(null!);
        var actText = () => _resetTemplate.RenderText(null!);

        actSubject.Should().Throw<ArgumentNullException>();
        actHtml.Should().Throw<ArgumentNullException>();
        actText.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PasswordResetTemplate_Throws_When_ResetUrlIsNullOrEmpty(string? invalidUrl)
    {
        var model = new PasswordResetTemplateModel(invalidUrl!, "John Doe");

        var act = () => _resetTemplate.RenderHtml(model);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Reset URL*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void PasswordResetTemplate_Throws_When_ExpiryMinutesIsNotPositive(int invalidExpiry)
    {
        var model = new PasswordResetTemplateModel("https://example.com/reset?token=abc", "John Doe", invalidExpiry);

        var act = () => _resetTemplate.RenderHtml(model);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Expiry*");
    }

    [Fact]
    public void SecurityNoticeTemplate_Throws_When_ModelIsNull()
    {
        var actSubject = () => _noticeTemplate.BuildSubject(null!);
        var actHtml = () => _noticeTemplate.RenderHtml(null!);
        var actText = () => _noticeTemplate.RenderText(null!);

        actSubject.Should().Throw<ArgumentNullException>();
        actHtml.Should().Throw<ArgumentNullException>();
        actText.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SecurityNoticeTemplate_Throws_When_TitleIsNullOrEmpty(string? invalidTitle)
    {
        var model = new SecurityNoticeTemplateModel(invalidTitle!, "Message", "John", DateTimeOffset.UtcNow);

        var act = () => _noticeTemplate.RenderHtml(model);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Title*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SecurityNoticeTemplate_Throws_When_MessageIsNullOrEmpty(string? invalidMessage)
    {
        var model = new SecurityNoticeTemplateModel("Alert", invalidMessage!, "John", DateTimeOffset.UtcNow);

        var act = () => _noticeTemplate.RenderHtml(model);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Message*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Templates_HandleNullOrWhitespaceDisplayName_WithSafeDefault(string? emptyDisplayName)
    {
        var otpModel = new RegistrationOtpTemplateModel("654321", emptyDisplayName!);
        var resetModel = new PasswordResetTemplateModel("https://example.com/reset", emptyDisplayName!);
        var noticeModel = new SecurityNoticeTemplateModel("Alert", "Something happened", emptyDisplayName!, DateTimeOffset.UtcNow);

        var otpHtml = _otpTemplate.RenderHtml(otpModel);
        var resetHtml = _resetTemplate.RenderHtml(resetModel);
        var noticeHtml = _noticeTemplate.RenderHtml(noticeModel);

        otpHtml.Should().Contain("Hi there,");
        resetHtml.Should().Contain("Hi there,");
        noticeHtml.Should().Contain("Hi there,");

        var otpText = _otpTemplate.RenderText(otpModel);
        var resetText = _resetTemplate.RenderText(resetModel);
        var noticeText = _noticeTemplate.RenderText(noticeModel);

        otpText.Should().Contain("Hi there,");
        resetText.Should().Contain("Hi there,");
        noticeText.Should().Contain("Hi there,");
    }

    [Fact]
    public void RegistrationOtpTemplate_EscapesDynamicInputs_InHtml_WhileRetainingRawTextInPlainText()
    {
        const string maliciousName = "<script>alert('xss')</script>";
        const string specialOtp = "12&34<5>";

        var model = new RegistrationOtpTemplateModel(specialOtp, maliciousName);

        var html = _otpTemplate.RenderHtml(model);
        var text = _otpTemplate.RenderText(model);

        // HTML MUST escape script tags and special characters
        html.Should().NotContain("<script>");
        html.Should().NotContain("</script>");
        html.Should().Contain("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;");
        html.Should().Contain("12&amp;34&lt;5&gt;");

        // Plain-text MUST retain raw parameters without HTML entities
        text.Should().Contain(maliciousName);
        text.Should().Contain(specialOtp);
        text.Should().NotContain("&lt;");
        text.Should().NotContain("&gt;");
        text.Should().NotContain("&amp;");
    }

    [Fact]
    public void PasswordResetTemplate_EscapesDynamicInputs_InHtml()
    {
        const string maliciousName = "Jane \"Hacker\" & Co <admin>";
        const string maliciousUrl = "https://example.com/reset?token=xyz&sig=123\" onclick=\"alert('pwned')";

        var model = new PasswordResetTemplateModel(maliciousUrl, maliciousName);

        var html = _resetTemplate.RenderHtml(model);
        var text = _resetTemplate.RenderText(model);

        html.Should().NotContain("\" onclick=");
        html.Should().Contain("&quot; onclick=&quot;");
        html.Should().NotContain("<admin>");
        html.Should().Contain(WebUtility.HtmlEncode(maliciousName));
        html.Should().Contain(WebUtility.HtmlEncode(maliciousUrl));

        text.Should().Contain(maliciousName);
        text.Should().Contain(maliciousUrl);
    }

    [Fact]
    public void SecurityNoticeTemplate_EscapesTitleAndMessage_InHtml()
    {
        const string dynamicTitle = "Suspicious login from <IP> & \"Device\"";
        const string dynamicMessage = "Someone tried accessing <b>/admin</b> with 'root' privileges.";
        const string dynamicName = "Bob <o'reilly>";

        var model = new SecurityNoticeTemplateModel(dynamicTitle, dynamicMessage, dynamicName, DateTimeOffset.UtcNow);

        var html = _noticeTemplate.RenderHtml(model);
        var text = _noticeTemplate.RenderText(model);

        html.Should().NotContain("<IP>");
        html.Should().NotContain("<b>/admin</b>");
        html.Should().Contain(WebUtility.HtmlEncode(dynamicTitle));
        html.Should().Contain(WebUtility.HtmlEncode(dynamicMessage));
        html.Should().Contain(WebUtility.HtmlEncode(dynamicName));

        text.Should().Contain(dynamicTitle);
        text.Should().Contain(dynamicMessage);
        text.Should().Contain(dynamicName);
    }
}
