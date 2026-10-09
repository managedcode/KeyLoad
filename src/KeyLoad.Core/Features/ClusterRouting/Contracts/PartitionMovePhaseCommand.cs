using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PhaseCommandAlias)]
internal sealed record PartitionMovePhaseCommand(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(4)] AtomicPartitionPlacementResolution SourcePlacement,
    [property: Orleans.Id(5)] PhysicalShardRecord DestinationOwner,
    [property: Orleans.Id(6)] string ControlIntentDigest,
    [property: Orleans.Id(7)] PartitionMovePeerStage Stage,
    [property: Orleans.Id(8)] int PageOrdinal,
    [property: Orleans.Id(9)] ReadOnlyMemory<byte> Body,
    [property: Orleans.Id(10)] Guid? GrantId = null,
    [property: Orleans.Id(11)] ImmutableArray<ResourceDefinition> Resources = default,
    [property: Orleans.Id(12), System.Text.Json.Serialization.JsonIgnore] PartitionMoveReceiverEffectAdmission? ReceiverEffectAdmission = null);
