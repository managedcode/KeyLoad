namespace KeyLoad;

/// <summary>Records the actual historical source and new identity of one native restore.</summary>
/// <param name="Version">Current generated metadata version.</param>
/// <param name="SourceIncarnation">Verified source store incarnation.</param>
/// <param name="RestoredIncarnation">New owning store incarnation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionRosterRestoreIdentityFields.Alias)]
public sealed record AtomicPartitionRosterRestoreIdentity(
    [property: Orleans.Id(AtomicPartitionRosterRestoreIdentityFields.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionRosterRestoreIdentityFields.SourceIncarnation)] Guid SourceIncarnation,
    [property: Orleans.Id(AtomicPartitionRosterRestoreIdentityFields.RestoredIncarnation)] Guid RestoredIncarnation);

internal static class AtomicPartitionRosterRestoreIdentityFields
{
    internal const string Alias = "keyload.backup.atomic-partition-roster-restore-identity.v1";
    internal const uint Version = 0;
    internal const uint SourceIncarnation = 1;
    internal const uint RestoredIncarnation = 2;
}
