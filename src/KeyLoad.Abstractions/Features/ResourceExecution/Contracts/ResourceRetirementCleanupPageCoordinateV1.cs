namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementCleanupAttemptContract.CoordinateAlias)]
internal sealed record ResourceRetirementCleanupPageCoordinateV1(
    [property: Orleans.Id(ResourceRetirementCleanupAttemptContract.PartitionId)] PartitionRef Partition,
    [property: Orleans.Id(ResourceRetirementCleanupAttemptContract.OrdinalId)] long Ordinal);
