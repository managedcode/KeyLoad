namespace KeyLoad.Core;

/// <summary>Centrally configured TimeSeries execution and admission policy.</summary>
[ConfigurationOptions]
public sealed record TimeSeriesExecutionOptions
{
    /// <summary>The centrally bound scenario section.</summary>
    public const string SectionName = "KeyLoad:TimeSeriesExecution";
    /// <summary>The startup rejection detail for invalid scenario settings.</summary>
    public const string ValidationMessage = "The TimeSeries execution settings are invalid.";
    private const int DefaultMaximumAppendSamples = 10_000;
    private const int MinimumWorkCount = 1;

    /// <summary>Maximum samples admitted to one atomic append.</summary>
    public int MaximumAppendSamples { get; init; } = DefaultMaximumAppendSamples;

    /// <summary>Whether sample admission remains within the current append conformance bound.</summary>
    public bool IsValid() => MaximumAppendSamples is >= MinimumWorkCount and <= DefaultMaximumAppendSamples;

    /// <summary>Rejects invalid settings before operation admission.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
