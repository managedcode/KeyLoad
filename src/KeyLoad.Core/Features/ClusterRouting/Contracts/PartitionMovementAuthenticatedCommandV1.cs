using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>The mandatory native movement root; logical identity remains the original phase.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionResourceLifetimeContract.CommandAlias)]
internal sealed record PartitionMovementAuthenticatedCommandV1(
    [property: Orleans.Id(PartitionResourceLifetimeContract.CommandOriginalId)] PartitionMovePhaseCommand Original,
    [property: Orleans.Id(PartitionResourceLifetimeContract.CommandLifetimeProofsId)] ImmutableArray<PartitionMoveLifetimeProofV1> LifetimeProofs);
