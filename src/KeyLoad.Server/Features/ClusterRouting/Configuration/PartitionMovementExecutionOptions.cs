namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Opts the configured independent two-owner topology into private movement execution.</summary>
[ConfigurationOptions]
internal sealed class PartitionMovementExecutionOptions
{
    internal const string SectionName = "KeyLoad:PartitionMovementExecution";
    internal const string ValidationMessage = "Partition movement requires a typed execution configuration.";

    /// <summary>Enables configured movement receivers; default RF3 admission remains closed.</summary>
    public bool Enabled { get; set; }
}
