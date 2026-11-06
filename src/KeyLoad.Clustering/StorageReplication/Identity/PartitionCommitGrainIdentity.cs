namespace KeyLoad.Clustering;

/// <summary>Creates the one case-sensitive address of an atomic partition's command owner.</summary>
public static class PartitionCommitGrainIdentity
{
    private const string GuidFormat = "N";
    private const string Separator = ":";

    /// <summary>Binds the persisted database incarnation and canonical atomic partition identity.</summary>
    public static string For(Guid incarnation, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: BCL validation checks the original PartitionRef instance. */;
        return string.Concat(DatabaseGrainIdentity.For(incarnation).ToString(GuidFormat), Separator, partition.AtomicPartitionId);
    }
}
