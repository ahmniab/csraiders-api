using CsRaiders.Api.Application.Common.EmailTemplates.Models;
using CsRaiders.Api.Application.Common.EmailTemplates.Templates;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Exceptions;
using CsRaiders.Api.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace CsRaiders.Api.Application.UnitTests.Security;

public class TemplatedEmailQueueTests
{
    private readonly EmailTemplateService _templateService = new();
    private readonly RegistrationOtpTemplate _otpTemplate = new();
    private readonly PasswordResetTemplate _resetTemplate = new();
    private readonly SecurityNoticeTemplate _noticeTemplate = new();

    private sealed class FakeEmailQueue : IEmailQueue
    {
        public bool AcceptMessages { get; set; } = true;
        public List<EmailMessage> EnqueuedMessages { get; } = [];

        public bool TryEnqueue(EmailMessage message)
        {
            if (!AcceptMessages)
                return false;

            EnqueuedMessages.Add(message);
            return true;
        }

        public async IAsyncEnumerable<EmailMessage> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var message in EnqueuedMessages)
            {
                yield return message;
            }
            await Task.CompletedTask;
        }
    }

    [Fact]
    public void TryEnqueueTemplate_Extension_EnqueuesRenderedMessage_WhenQueueHasCapacity()
    {
        var queue = new FakeEmailQueue();
        var model = new RegistrationOtpTemplateModel("554433", "Grace");

        var result = queue.TryEnqueueTemplate(
            _templateService,
            "grace@example.com",
            _otpTemplate,
            model,
            "corr-reg-1");

        result.Should().BeTrue();
        queue.EnqueuedMessages.Should().HaveCount(1);

        var enqueued = queue.EnqueuedMessages[0];
        enqueued.To.Should().Be("grace@example.com");
        enqueued.Subject.Should().Be("Verify your CS Raiders account");
        enqueued.Purpose.Should().Be(EmailPurpose.RegistrationOtp);
        enqueued.CorrelationId.Should().Be("corr-reg-1");
        enqueued.BodyHtml.Should().Contain("554433");
        enqueued.BodyHtml.Should().Contain("Grace");
        enqueued.BodyText.Should().Contain("554433");
        enqueued.BodyText.Should().Contain("Grace");
    }

    [Fact]
    public void TryEnqueueTemplate_Extension_ReturnsFalseImmediately_WhenQueueIsSaturated()
    {
        var queue = new FakeEmailQueue { AcceptMessages = false };
        var model = new RegistrationOtpTemplateModel("554433", "Grace");

        var result = queue.TryEnqueueTemplate(
            _templateService,
            "grace@example.com",
            _otpTemplate,
            model);

        result.Should().BeFalse();
        queue.EnqueuedMessages.Should().BeEmpty();
    }

    [Fact]
    public void TryEnqueueTemplate_Extension_ValidatesArguments()
    {
        var queue = new FakeEmailQueue();
        var model = new RegistrationOtpTemplateModel("123456", "User");

        var actNullQueue = () => TemplatedEmailQueueExtensions.TryEnqueueTemplate(
            null!, _templateService, "test@test.com", _otpTemplate, model);
        var actNullService = () => queue.TryEnqueueTemplate(
            null!, "test@test.com", _otpTemplate, model);
        var actNullTemplate = () => queue.TryEnqueueTemplate<RegistrationOtpTemplateModel>(
            _templateService, "test@test.com", null!, model);
        var actNullModel = () => queue.TryEnqueueTemplate(
            _templateService, "test@test.com", _otpTemplate, null!);

        actNullQueue.Should().Throw<ArgumentNullException>();
        actNullService.Should().Throw<ArgumentNullException>();
        actNullTemplate.Should().Throw<ArgumentNullException>();
        actNullModel.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TemplatedEmailQueue_Facade_RendersAndEnqueuesAtomically()
    {
        var queue = new FakeEmailQueue();
        var facade = new TemplatedEmailQueue(queue, _templateService);
        var model = new PasswordResetTemplateModel("https://csraiders.com/reset?token=xyz", "Henry");

        var result = facade.TryEnqueueTemplate(
            "  Henry@Example.Com  ",
            _resetTemplate,
            model,
            "corr-pwd-99");

        result.Should().BeTrue();
        queue.EnqueuedMessages.Should().HaveCount(1);

        var message = queue.EnqueuedMessages[0];
        message.To.Should().Be("henry@example.com");
        message.Subject.Should().Be("Reset your CS Raiders password");
        message.Purpose.Should().Be(EmailPurpose.PasswordReset);
        message.CorrelationId.Should().Be("corr-pwd-99");
        message.BodyHtml.Should().Contain("Reset Password");
        message.BodyText.Should().Contain("https://csraiders.com/reset?token=xyz");
    }

    [Fact]
    public void TemplatedEmailQueue_Facade_ReturnsFalseImmediately_WhenQueueIsSaturated()
    {
        var queue = new FakeEmailQueue { AcceptMessages = false };
        var facade = new TemplatedEmailQueue(queue, _templateService);
        var model = new SecurityNoticeTemplateModel("Lockout", "Account locked", "Isla", DateTimeOffset.UtcNow);

        var result = facade.TryEnqueueTemplate(
            "isla@example.com",
            _noticeTemplate,
            model);

        result.Should().BeFalse();
        queue.EnqueuedMessages.Should().BeEmpty();
    }

    [Fact]
    public void TemplatedEmailQueue_Facade_TryEnqueue_ForwardsMessageDirectly()
    {
        var queue = new FakeEmailQueue();
        var facade = new TemplatedEmailQueue(queue, _templateService);
        var message = new EmailMessage("direct@test.com", "Direct Subject", "<p>hi</p>", "hi", EmailPurpose.AccountNotice);

        var result = facade.TryEnqueue(message);

        result.Should().BeTrue();
        queue.EnqueuedMessages.Should().ContainSingle().Which.Should().BeSameAs(message);
    }

    [Fact]
    public void TemplatedEmailQueue_Facade_RejectsInvalidEmail_BeforeQueueing()
    {
        var queue = new FakeEmailQueue();
        var facade = new TemplatedEmailQueue(queue, _templateService);
        var model = new RegistrationOtpTemplateModel("123456", "User");

        var actEmpty = () => facade.TryEnqueueTemplate("  ", _otpTemplate, model);
        var actInvalidSyntax = () => facade.TryEnqueueTemplate("not-an-email", _otpTemplate, model);

        actEmpty.Should().Throw<ArgumentException>();
        actInvalidSyntax.Should().Throw<DomainException>();
        queue.EnqueuedMessages.Should().BeEmpty();
    }
}
