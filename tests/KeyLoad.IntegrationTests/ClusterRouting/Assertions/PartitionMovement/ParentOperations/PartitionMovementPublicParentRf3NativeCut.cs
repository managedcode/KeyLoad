using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Original native store bytes and validated retained parent rows after all six owned processes exited.</summary>
internal sealed record PartitionMovementPublicParentRf3NativeCut(string Node, long StorePosition, string[] Rows,
    PartitionMoveParentHeader? Header, PartitionMoveParentPhase? Pending, PartitionMoveParentPhase? LastIssued,
    PartitionMoveParentPhase? OriginalPhase, PartitionMoveExpiredRetireCancellation? RetireCancellation,
    OperationResult? NativeCancellationResult, OperationResult? OriginalNativeResult,
    PartitionMoveReceiverIssuance? OriginalReceiverIssuance, PartitionMoveParentPhase[] SettledStagePages,
    PartitionMovePhaseGrant? OriginalControlGrant, long OutstandingMoveGrants,
    long? OutstandingPrincipalGrants, long OutstandingDatabaseGrants);
