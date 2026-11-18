using System.Diagnostics;
using System.Text;
using CsRaiders.Api.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CsRaiders.Api.Application.Common.Diagnostics;

/// <summary>
/// Scoped step timer for the request pipeline. Tracks a tree of nested steps and
/// logs an inclusive/exclusive time breakdown once per request, when the outermost
/// step completes. Not thread-safe by design: pipeline steps run sequentially per request.
/// </summary>
public sealed class RequestStepTimer(
    ILogger<RequestStepTimer> logger,
    IOptions<PipelineTimingOptions> options) : IRequestStepTimer
{
    private static readonly IDisposable DisabledScope = new NoOpScope();

    private readonly PipelineTimingOptions _options = options.Value;
    private readonly List<Step> _stack = [];
    private readonly List<StepTiming> _completed = [];
    private string? _method;
    private string? _path;
    private int? _statusCode;

    /// <summary>Flat, completion-ordered view of all completed steps (exposed for tests and diagnostics).</summary>
    public IReadOnlyList<StepTiming> CompletedSteps => _completed;

    public IDisposable Track(string stepName)
    {
        if (!_options.Enabled) return DisabledScope;

        var parent = _stack.Count > 0 ? _stack[^1] : null;
        var step = new Step(this, stepName, Stopwatch.GetTimestamp(), parent);
        _stack.Add(step);
        return step;
    }

    public void SetRequestContext(string method, string path)
    {
        _method ??= method;
        _path ??= path;
    }

    public void MarkResponse(int statusCode) => _statusCode = statusCode;

    private void Complete(Step step)
    {
        var depth = _stack.LastIndexOf(step);
        if (depth >= 0) _stack.RemoveAt(depth);

        step.Parent?.AddChild(step);
        _completed.Add(new StepTiming(step.Name, step.DurationMs, step.ExclusiveMs, Math.Max(depth, 0)));

        // The outermost step finished: the pipeline ran to completion (or short-circuited).
        // Emit one summary entry per request.
        if (step.Parent is null) Emit(step);
    }

    private void Emit(Step root)
    {
        logger.LogInformation(
            "Request pipeline timing: {Method} {Path} -> {Status} in {TotalMs:0.###} ms{Breakdown}",
            _method ?? "-", _path ?? "-", _statusCode?.ToString() ?? "-", root.DurationMs, Render(root));
    }

    private string Render(Step root)
    {
        var sb = new StringBuilder();
        Append(root, depth: 0, sb);
        return sb.ToString();
    }

    private void Append(Step step, int depth, StringBuilder sb)
    {
        sb.AppendLine();
        sb.Append("  ");
        sb.Append(' ', depth * 2);
        sb.Append($"{step.Name,-34}{step.DurationMs,10:0.###} ms");
        if (step.ChildrenCount > 0)
            sb.Append($"  (self {step.ExclusiveMs:0.###} ms)");
        if (step.ExclusiveMs >= _options.SlowStepThresholdMs)
            sb.Append("  <== slow");

        foreach (var child in step.Children)
            Append(child, depth + 1, sb);
    }

    /// <summary>A single completed pipeline step: inclusive duration, exclusive (self) duration and tree depth.</summary>
    public sealed record StepTiming(string Name, double InclusiveMs, double ExclusiveMs, int Depth);

    private sealed class Step(RequestStepTimer owner, string name, long startTimestamp, Step? parent) : IDisposable
    {
        private readonly long _startTimestamp = startTimestamp;
        private readonly List<Step> _children = [];
        private bool _completed;

        public string Name { get; } = name;
        public Step? Parent { get; } = parent;
        public IReadOnlyList<Step> Children => _children;
        public int ChildrenCount => _children.Count;
        public double DurationMs { get; private set; }
        public double ExclusiveMs => DurationMs - _children.Sum(child => child.DurationMs);

        public void AddChild(Step child) => _children.Add(child);

        public void Dispose()
        {
            if (_completed) return;
            _completed = true;
            DurationMs = (Stopwatch.GetTimestamp() - _startTimestamp) * 1000d / Stopwatch.Frequency;
            owner.Complete(this);
        }
    }

    private sealed class NoOpScope : IDisposable
    {
        public void Dispose() { }
    }
}
