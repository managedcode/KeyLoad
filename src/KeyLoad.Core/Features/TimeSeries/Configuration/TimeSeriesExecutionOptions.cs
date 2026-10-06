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
    private const int DefaultHashChunkBytes = 65_536;
    private const int DefaultTextCancellationCheckIntervalCodeUnits = 16_383;
    private const int MinimumWorkCount = 1;

    /// <summary>Maximum samples admitted to one atomic append.</summary>
    public int MaximumAppendSamples { get; init; } = DefaultMaximumAppendSamples;

    /// <summary>The maximum bytes hashed between codec cancellation and deadline checks.</summary>
    public int HashChunkBytes { get; init; } = DefaultHashChunkBytes;

    /// <summary>The maximum text code units walked between codec cancellation and deadline checks.</summary>
    public int TextCancellationCheckIntervalCodeUnits { get; init; } = DefaultTextCancellationCheckIntervalCodeUnits;

    /// <summary>Whether admission and codec execution stay within the current qualified policy bounds.</summary>
    public bool IsValid() => MaximumAppendSamples is >= MinimumWorkCount and <= DefaultMaximumAppendSamples
        && HashChunkBytes is >= MinimumWorkCount and <= DefaultHashChunkBytes
        && TextCancellationCheckIntervalCodeUnits is >= MinimumWorkCount and <= DefaultTextCancellationCheckIntervalCodeUnits;

    /// <summary>Rejects invalid settings before operation admission.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
