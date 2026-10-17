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
