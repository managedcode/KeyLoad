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
    private const int MaximumPreparationHours = 24;

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

    /// <summary>Checks consistent, positive memory admission and a native timer-safe deadline.</summary>
    /// <returns>Whether native fixture ownership can begin.</returns>
    public bool IsValid() => PreparationTimeout > TimeSpan.Zero
        && PreparationTimeout <= TimeSpan.FromHours(MaximumPreparationHours)
        && MaximumProcessBytes > 0 && RequiredHeadroomBytes > 0
        && MaximumProcessBytes <= long.MaxValue - RequiredHeadroomBytes
        && MinimumQualificationCapacityBytes >= MaximumProcessBytes + RequiredHeadroomBytes
        && MinimumMutableSegmentRecords > 0;

    /// <summary>Rejects invalid standalone composition before storage allocation.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new OptionsValidationException(SectionName, typeof(ScaledStorageExecutionOptions), [ValidationMessage]); }
    }
}
