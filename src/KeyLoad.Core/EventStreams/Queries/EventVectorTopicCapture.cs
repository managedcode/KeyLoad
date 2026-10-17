namespace KeyLoad.Core;

internal sealed record EventVectorTopicCapture(ResourceDefinition Resource,
    AtomicPartitionPlacementResolution Placement, TopicHead? Head, EventVectorCoverageRow? HeadRow);
