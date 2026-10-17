namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record TenantCapacityPlacementRf3IntentCollisionState(
    Guid PersistId, PersistPlacementIntentRequest Request, PlacementIntentV1 Pending,
    PartitionMoveRequest Move, Guid CollisionId, PersistPlacementIntentRequest CollisionRequest,
    PartitionMoveRequest CollisionMove);
