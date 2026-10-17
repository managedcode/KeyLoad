using System.Collections.Immutable;

namespace KeyLoad.Core;

internal sealed record EventVectorStreamSetCapture(ResourceDefinition Resource,
    AtomicPartitionPlacementResolution Placement, ImmutableArray<EventVectorStreamHeadCapture> Heads);
