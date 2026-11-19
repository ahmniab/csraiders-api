using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CsRaiders.Api.Application.UnitTests.Diagnostics;

public sealed class RequestStepTimerTests
{
    private static RequestStepTimer CreateTimer(bool enabled = true, double slowThresholdMs = 100) =>
        new(NullLogger<RequestStepTimer>.Instance,
            Options.Create(new PipelineTimingOptions { Enabled = enabled, SlowStepThresholdMs = slowThresholdMs }));

    [Fact]
    public void Track_with_disabled_options_records_nothing()
    {
        var timer = CreateTimer(enabled: false);

        using (timer.Track("outer"))
        {
            using (timer.Track("inner")) { /* no work */ }
        }

        timer.CompletedSteps.Should().BeEmpty();
    }

    [Fact]
    public void Track_reports_nested_steps_with_inclusive_exclusive_and_depth()
    {
        var timer = CreateTimer();

        using (timer.Track("outer"))
        {
            using (timer.Track("child")) Thread.Sleep(20);
            Thread.Sleep(20);
        }

        var steps = timer.CompletedSteps;

        steps.Should().HaveCount(2);
        steps[0].Depth.Should().Be(1);
        steps[0].Name.Should().Be("child");
        steps[0].InclusiveMs.Should().BeGreaterThan(15);

        steps[1].Depth.Should().Be(0);
        steps[1].Name.Should().Be("outer");
        steps[1].InclusiveMs.Should().BeGreaterThan(steps[0].InclusiveMs + 15);
        steps[1].ExclusiveMs.Should().Be(steps[1].InclusiveMs - steps[0].InclusiveMs);
        steps[1].ExclusiveMs.Should().BeGreaterThan(15);
    }

    [Fact]
    public void Track_records_sequential_siblings_in_completion_order()
    {
        var timer = CreateTimer();

        using (timer.Track("pipeline"))
        {
            using (timer.Track("first")) { }
            using (timer.Track("second")) { }
            using (timer.Track("third")) { }
        }

        timer.CompletedSteps.Select(s => s.Name)
            .Should().ContainInConsecutiveOrder("first", "second", "third", "pipeline");
    }

    [Fact]
    public void Track_dispose_is_idempotent()
    {
        var timer = CreateTimer();

        var step = timer.Track("step");
        step.Dispose();
        step.Dispose();

        timer.CompletedSteps.Should().ContainSingle();
    }

    [Fact]
    public void Outermost_step_completion_emits_one_summary_per_request()
    {
        var logger = new RecordingLogger();
        var timer = new RequestStepTimer(logger,
            Options.Create(new PipelineTimingOptions { Enabled = true }));

        timer.SetRequestContext("POST", "/api/v1/auth/login");

        using (timer.Track("ForwardedHeaders"))
        {
            using (timer.Track("Endpoint"))
            {
                timer.MarkResponse(200);
            }
        }

        logger.Messages.Should().HaveCount(1);
        var entry = logger.Messages[0];
        entry.Should().Contain("Request pipeline timing");
        entry.Should().Contain("POST /api/v1/auth/login -> 200");
        entry.Should().Contain("ForwardedHeaders");
        entry.Should().Contain("Endpoint");
    }

    private sealed class RecordingLogger : ILogger<RequestStepTimer>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
