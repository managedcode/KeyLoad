namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Bounds admission and execution mutation attempts in one raw fixture.</summary>
[ConfigurationOptions]
public sealed class RawStorageExecutionOptions
{
    /// <summary>The execution raw benchmark execution section.</summary>
    public const string SectionName = "RawStorageExecution";
    /// <summary>Rejection raised before raw fixture storage ownership.</summary>
    public const string ValidationMessage = "Raw storage records and mutation attempts must remain positive and within their existing ceilings.";
    private const int MutationAttemptCeiling = 65_536;
    private const int RecordCountCeiling = 4096;

    /// <summary>The maximum attempted execution mutations including corpus seeding.</summary>
    public int MaximumWrites { get; set; } = MutationAttemptCeiling;
    /// <summary>The admitted raw-fixture record count.</summary>
    public int MaximumRecords { get; set; } = RecordCountCeiling;

    /// <summary>Checks fixture admission within the existing execution microbenchmark ceilings.</summary>
    /// <returns>Whether raw execution ownership can start.</returns>
    public bool IsValid() => MaximumWrites is > 0 and <= MutationAttemptCeiling
        && MaximumRecords is > 0 and <= RecordCountCeiling;

    /// <summary>Rejects invalid direct fixture policy before allocation.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumWrites), ValidationMessage);
        }
    }
}
