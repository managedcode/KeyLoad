namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Bounds admission and native mutation attempts in one raw fixture.</summary>
[ConfigurationOptions]
public sealed class RawStorageExecutionOptions
{
    private const int IsValidMaximumWritesEmptyCount = 0;
    private const int IsValidMaximumRecordsEmptyCount = 0;

    /// <summary>The native raw benchmark execution section.</summary>
    public const string SectionName = "RawStorageExecution";
    /// <summary>Rejection raised before raw fixture storage ownership.</summary>
    public const string ValidationMessage = "Raw storage records and mutation attempts must remain positive and within their existing ceilings.";
    private const int MutationAttemptCeiling = 65_536;
    private const int RecordCountCeiling = 4096;

    /// <summary>The maximum attempted native mutations including corpus seeding.</summary>
    public int MaximumWrites { get; set; } = MutationAttemptCeiling;
    /// <summary>The admitted raw-fixture record count.</summary>
    public int MaximumRecords { get; set; } = RecordCountCeiling;

    /// <summary>Checks fixture admission within the existing native microbenchmark ceilings.</summary>
    /// <returns>Whether raw native ownership can start.</returns>
    public bool IsValid() => MaximumWrites is > IsValidMaximumWritesEmptyCount and <= MutationAttemptCeiling
        && MaximumRecords is > IsValidMaximumRecordsEmptyCount and <= RecordCountCeiling;

    /// <summary>Rejects invalid direct fixture policy before allocation.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new ArgumentOutOfRangeException(nameof(MaximumWrites), ValidationMessage); }
    }
}
