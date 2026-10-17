using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PublishBodyAlias)]
internal sealed record PartitionMovePublishBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMovePublishedPlacement Publication,
    [property: Orleans.Id(3)] ImmutableArray<ResourceDefinition> Resources);
