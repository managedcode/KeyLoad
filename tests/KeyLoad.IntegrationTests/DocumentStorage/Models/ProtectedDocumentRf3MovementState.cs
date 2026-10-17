namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Actual public movement result and its unchanged original physical owners.</summary>
internal sealed record ProtectedDocumentRf3MovementState(PartitionMoveRequest Request,
    PartitionMoveResult Receipt, PhysicalShardRecord Source, PhysicalShardRecord Target, NodeStatus TargetCut);
