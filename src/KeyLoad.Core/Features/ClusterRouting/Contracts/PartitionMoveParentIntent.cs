namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Binds admitted public parent ownership without changing standalone primitive intent bytes.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveParentIntentAlias)]
internal sealed record PartitionMoveParentIntent(
    [property: Orleans.Id(0)] PartitionMoveIntent OriginalIntent,
    [property: Orleans.Id(1)] bool ParentCheckpointRequired);
