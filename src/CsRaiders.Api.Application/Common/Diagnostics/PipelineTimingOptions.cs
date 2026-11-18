namespace CsRaiders.Api.Application.Common.Diagnostics;

/// <summary>
/// Options for the request pipeline step timing diagnostics.
/// Bound from the "PipelineTiming" configuration section.
/// </summary>
public sealed class PipelineTimingOptions
{
    /// <summary>Enables per-step request pipeline timing summaries. Default: enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Steps whose own (exclusive, i.e. excluding nested steps) duration exceeds this
    /// threshold are flagged as slow in the summary. Default: 100 ms.
    /// </summary>
    public double SlowStepThresholdMs { get; set; } = 100;
}
