namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Retains the destination's original local coverage while the original native image is installed.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ResourceOwnerInventoryMovementProtocol.ImportAlias)]
internal sealed record ResourceOwnerInventoryImportOriginV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] ResourceOwnerInventoryOriginV1? PriorOrigin);
