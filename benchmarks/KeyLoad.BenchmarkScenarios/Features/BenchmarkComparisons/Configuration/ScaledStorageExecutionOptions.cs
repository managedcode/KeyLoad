using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Bounds standalone scaled-fixture preparation and retained process memory.</summary>
[ConfigurationOptions]
public sealed class ScaledStorageExecutionOptions
{
    /// <summary>The native benchmark execution section.</summary>
    public const string SectionName = "ScaledStorageExecution";
    /// <summary>Safe rejection before scaled-fixture admission.</summary>
    public const string ValidationMessage = "Scaled storage preparation, memory and mutable-segment budgets must be positive and consistent.";
    private const int DefaultPreparationMinutes = 20;
    private const long DefaultMaximumProcessBytes = 12_884_901_888;
    private const long DefaultRequiredHeadroomBytes = 2_147_483_648;
    private const long DefaultQualificationCapacityBytes = 15_032_385_536;
    private const int DefaultMutableSegmentRecords = 1000;
    private const int MaximumCancellationCheckInterval = 256;
    private const int MaximumRecordsPerValueChunk = 4096;
    private const int MaximumPreparationHours = 24;
    private const int MinimumPositiveBudget = 0;
    private static readonly TimeSpan MaximumPreparationTimeout = TimeSpan.FromHours(MaximumPreparationHours);

    /// <summary>The full preparation deadline including capacity admission and verification.</summary>
    public TimeSpan PreparationTimeout { get; set; } = TimeSpan.FromMinutes(DefaultPreparationMinutes);
    /// <summary>The observed process memory ceiling for native scaled fixtures.</summary>
    public long MaximumProcessBytes { get; set; } = DefaultMaximumProcessBytes;
    /// <summary>The available process memory reserved beyond measured retention.</summary>
    public long RequiredHeadroomBytes { get; set; } = DefaultRequiredHeadroomBytes;
    /// <summary>The available memory required for qualification-sized fixture admission.</summary>
    public long MinimumQualificationCapacityBytes { get; set; } = DefaultQualificationCapacityBytes;
    /// <summary>The minimum native mutable-segment capacity before corpus-based sizing.</summary>
    public int MinimumMutableSegmentRecords { get; set; } = DefaultMutableSegmentRecords;
    /// <summary>The maximum operations between preparation cancellation checks.</summary>
    public int CancellationCheckInterval { get; set; } = MaximumCancellationCheckInterval;
    /// <summary>The bounded number of values retained in one contiguous allocation.</summary>
    public int RecordsPerValueChunk { get; set; } = MaximumRecordsPerValueChunk;

    /// <summary>Checks consistent, positive memory admission and a native timer-safe deadline.</summary>
    /// <returns>Whether native fixture ownership can begin.</returns>
    public bool IsValid() => PreparationTimeout > TimeSpan.Zero
        && PreparationTimeout <= MaximumPreparationTimeout
        && MaximumProcessBytes > MinimumPositiveBudget && RequiredHeadroomBytes > MinimumPositiveBudget
        && MaximumProcessBytes <= long.MaxValue - RequiredHeadroomBytes
        && MinimumQualificationCapacityBytes >= MaximumProcessBytes + RequiredHeadroomBytes
        && MinimumMutableSegmentRecords > MinimumPositiveBudget
        && CancellationCheckInterval > MinimumPositiveBudget && CancellationCheckInterval <= MaximumCancellationCheckInterval
        && RecordsPerValueChunk > MinimumPositiveBudget && RecordsPerValueChunk <= MaximumRecordsPerValueChunk;

    /// <summary>Rejects invalid standalone composition before storage allocation.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new OptionsValidationException(SectionName, typeof(ScaledStorageExecutionOptions), [ValidationMessage]); }
    }
}
