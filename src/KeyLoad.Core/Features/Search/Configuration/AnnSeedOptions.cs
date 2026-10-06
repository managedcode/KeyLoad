namespace KeyLoad.Core.Features.Search;

/// <summary>Central admission settings for a disposable authorized ANN source snapshot.</summary>
[ConfigurationOptions]
public sealed record AnnSeedOptions
{
    /// <summary>The centrally bound scenario section.</summary>
    public const string SectionName = "KeyLoad:AnnSeed";
    /// <summary>The existing ANN domain rejection for invalid seed bounds.</summary>
    public const string ValidationMessage = "The ANN seed limits are invalid.";
    internal const int DefaultMaxRecords = 5_000_000;
    internal const long DefaultMaxOwnedBytes = 268_435_456;
    internal const long DefaultMaxPeakBytes = 536_870_912;
    internal const long DefaultMaxWorkUnits = 1_000_000_000;
    private const int DefaultInitialRecordCapacity = 32;
    private const int MinimumCount = 1;
    private const long MinimumMemoryBytes = 1_024;
    private const long MaximumOwnedBytes = 8_589_934_592;
    private const long MaximumPeakBytes = 17_179_869_184;
    private const long MaximumWorkUnits = 1_000_000_000_000;

    /// <summary>Maximum retained source record count.</summary>
    public int MaxRecords { get; init; } = DefaultMaxRecords;
    /// <summary>Maximum retained modeled bytes.</summary>
    public long MaxOwnedBytes { get; init; } = DefaultMaxOwnedBytes;
    /// <summary>Maximum transient modeled bytes while copying source data.</summary>
    public long MaxPeakBytes { get; init; } = DefaultMaxPeakBytes;
    /// <summary>Maximum measured collection work units.</summary>
    public long MaxWorkUnits { get; init; } = DefaultMaxWorkUnits;
    /// <summary>Initial source record allocation target, clamped to MaxRecords.</summary>
    public int InitialRecordCapacity { get; init; } = DefaultInitialRecordCapacity;

    /// <summary>Whether configured bounds preserve the current source-snapshot contract.</summary>
    public bool IsValid() => MaxRecords is >= MinimumCount and <= DefaultMaxRecords
        && MaxOwnedBytes is >= MinimumMemoryBytes and <= MaximumOwnedBytes
        && MaxPeakBytes is >= MinimumMemoryBytes and <= MaximumPeakBytes
        && MaxWorkUnits is >= MinimumCount and <= MaximumWorkUnits
        && MaxPeakBytes >= MaxOwnedBytes
        && InitialRecordCapacity is >= MinimumCount and <= DefaultInitialRecordCapacity;

    /// <summary>Rejects invalid settings using the existing ANN domain error.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw Errors.Fail(ErrorCode.Validation, ValidationMessage);
        }
    }
}
