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
    private const int DefaultMaximumChunkWindows = 32;
    private const int DefaultMaximumChunkWindowRecords = 4_096;
    private const int DefaultMaximumChunkCorrections = 256;
    private const int DefaultMaximumPendingChunkWindows = 32;

    /// <summary>Maximum retained enrolled window identities per series, including dropped tombstones.</summary>
    public int MaximumChunkWindows { get; init; } = DefaultMaximumChunkWindows;
    /// <summary>Maximum canonical records represented in one enrolled window.</summary>
    public int MaximumChunkWindowRecords { get; init; } = DefaultMaximumChunkWindowRecords;
    /// <summary>Maximum immutable late correction records before a new generation merge.</summary>
    public int MaximumChunkCorrections { get; init; } = DefaultMaximumChunkCorrections;
    /// <summary>Maximum enrolled windows with pending durable maintenance per series.</summary>
    public int MaximumPendingChunkWindows { get; init; } = DefaultMaximumPendingChunkWindows;

    /// <summary>Maximum samples admitted to one atomic append.</summary>
    public int MaximumAppendSamples { get; init; } = DefaultMaximumAppendSamples;

    /// <summary>The maximum bytes hashed between codec cancellation and deadline checks.</summary>
    public int HashChunkBytes { get; init; } = DefaultHashChunkBytes;

    /// <summary>The maximum text code units walked between codec cancellation and deadline checks.</summary>
    public int TextCancellationCheckIntervalCodeUnits { get; init; } = DefaultTextCancellationCheckIntervalCodeUnits;

    /// <summary>Maximum retained bucket identities per series, including revision tombstones.</summary>
    public int MaximumRollupBuckets { get; init; } = TimeSeriesReadDefaults.MaxWindows;

    /// <summary>Whether admission and codec execution satisfy the central policy bounds.</summary>
    public bool IsValid() => MaximumAppendSamples is >= MinimumWorkCount and <= DefaultMaximumAppendSamples
        && MaximumChunkWindows is >= MinimumWorkCount and <= DefaultMaximumChunkWindows
        && MaximumChunkWindowRecords is >= MinimumWorkCount and <= DefaultMaximumChunkWindowRecords
        && MaximumChunkCorrections is >= MinimumWorkCount and <= DefaultMaximumChunkCorrections
        && MaximumPendingChunkWindows is >= MinimumWorkCount and <= DefaultMaximumPendingChunkWindows
        && MaximumPendingChunkWindows <= MaximumChunkWindows
        && HashChunkBytes is >= MinimumWorkCount and <= DefaultHashChunkBytes
        && MaximumRollupBuckets is >= MinimumWorkCount and <= TimeSeriesReadDefaults.MaxWindows
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
