using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Binds the complete admitted resource lifetime set to its full atomic partition.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionResourceLifetimeContract.CoverageAlias)]
internal sealed record ResourceLifetimeCoverageV1(
    [property: Orleans.Id(PartitionResourceLifetimeContract.CoveragePartitionId)] PartitionRef Partition,
    [property: Orleans.Id(PartitionResourceLifetimeContract.CoverageLifetimesId)] ImmutableArray<ResourceLifetimeV1> Lifetimes);
