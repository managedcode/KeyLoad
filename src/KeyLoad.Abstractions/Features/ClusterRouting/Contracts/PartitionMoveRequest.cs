namespace KeyLoad;

/// <summary>Selects the current administrator-controlled partition movement action.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveAliases.Mode)]
public enum PartitionMoveMode
{
    /// <summary>No movement action was selected; admission rejects this value.</summary>
    None = 0,
    /// <summary>Begins the exact bounded transfer and monotonic owner transition.</summary>
    Transfer = 1,
    /// <summary>Reconciles and continues the same durable move identity.</summary>
    Resume = 2,
    /// <summary>Abandons an unpublished move after its actual owners settle.</summary>
    Abort = 3
}

/// <summary>Requests a real controlled move; supplied physical fields are expectations, never grants.</summary>
/// <param name="MoveId">Stable administrator operation identity.</param>
/// <param name="Partition">Complete unchanged logical partition scope.</param>
/// <param name="DestinationPhysicalShardId">Expected registered destination RF3 owner.</param>
/// <param name="ExpectedPlacementRevision">Expected current committed PMAP revision.</param>
/// <param name="Mode">Original transfer, bounded resume or unpublished abort.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveAliases.Request)]
public sealed record PartitionMoveRequest(
    [property: Orleans.Id(0)] Guid MoveId,
    [property: Orleans.Id(1)] PartitionRef Partition,
    [property: Orleans.Id(2)] Guid DestinationPhysicalShardId,
    [property: Orleans.Id(3)] long ExpectedPlacementRevision,
    [property: Orleans.Id(4)] PartitionMoveMode Mode);
