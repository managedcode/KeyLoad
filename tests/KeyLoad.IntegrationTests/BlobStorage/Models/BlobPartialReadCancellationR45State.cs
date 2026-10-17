namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed record BlobPartialReadCancellationR45State(
    BlobBoundedRetirementPublicPublication.Published Publication,
    AtomicPartitionPlacementResolution Placement, OutboxStatus Outbox, BlobMetadata Previous);
