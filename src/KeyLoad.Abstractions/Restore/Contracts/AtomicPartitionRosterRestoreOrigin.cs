namespace KeyLoad;

/// <summary>Binds one unchanged historical roster row to a newly restored incarnation.</summary>
/// <param name="Version">Identifies the current generated metadata format.</param>
/// <param name="Partition">Complete logical partition scope of the retained row.</param>
/// <param name="SourceIncarnation">Actual incarnation of the verified source materialization.</param>
/// <param name="RestoredIncarnation">Actual new incarnation that owns this historical binding.</param>
/// <param name="AppliedUpperBound">Verified historical bound, never current replica authority.</param>
/// <param name="EntryDigest">SHA256 of the unchanged native roster-row bytes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionRosterRestoreOriginSerialization.Alias)]
public sealed record AtomicPartitionRosterRestoreOrigin(
    [property: Orleans.Id(AtomicPartitionRosterRestoreOriginFields.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionRosterRestoreOriginFields.Partition)] PartitionRef Partition,
    [property: Orleans.Id(AtomicPartitionRosterRestoreOriginFields.SourceIncarnation)] Guid SourceIncarnation,
    [property: Orleans.Id(AtomicPartitionRosterRestoreOriginFields.RestoredIncarnation)] Guid RestoredIncarnation,
    [property: Orleans.Id(AtomicPartitionRosterRestoreOriginFields.AppliedUpperBound)] long AppliedUpperBound,
    [property: Orleans.Id(AtomicPartitionRosterRestoreOriginFields.EntryDigest)] ReadOnlyMemory<byte> EntryDigest);

internal static class AtomicPartitionRosterRestoreOriginFields
{
    internal const uint Version = 0;
    internal const uint Partition = 1;
    internal const uint SourceIncarnation = 2;
    internal const uint RestoredIncarnation = 3;
    internal const uint AppliedUpperBound = 4;
    internal const uint EntryDigest = 5;
}
