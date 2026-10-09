namespace KeyLoad.Server;

/// <summary>Centrally validated node-local admission for native archive capture producers.</summary>
[ConfigurationOptions]
internal sealed class ClusterBackupExecutionOptions
{
    internal const string SectionName = "KeyLoad:ClusterBackupExecution";
    internal const string ValidationMessage = "Cluster backup capture admissions must be between one and four.";
    private const int MinimumAdmissions = 1;
    private const int DefaultMaximumAdmissions = 4;
    private const int MaximumAdmissionCeiling = 4;

    /// <summary>Maximum accepted capture producers, including trusted direct native calls.</summary>
    public int MaximumAdmissions { get; set; } = DefaultMaximumAdmissions;

    internal bool IsValid() => MaximumAdmissions is >= MinimumAdmissions and <= MaximumAdmissionCeiling;
}
