namespace KeyLoad;

/// <summary>Bounds and identifies the generated atomic-placement directory.</summary>
/// <param name="Version">The directory format version.</param>
/// <param name="Revision">The monotonic directory revision.</param>
/// <param name="ExplicitAssignmentCount">The number of explicit atomic assignments.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionPlacementAliases.Directory)]
public sealed record AtomicPartitionPlacementDirectoryV1(
    [property: Orleans.Id(AtomicPartitionPlacementDirectoryFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionPlacementDirectoryFieldIds.Revision)] long Revision,
    [property: Orleans.Id(AtomicPartitionPlacementDirectoryFieldIds.ExplicitAssignmentCount)] int ExplicitAssignmentCount);
