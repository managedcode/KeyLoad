using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityBounds
{
    internal static long Bound(PartitionMoveParentPhase value)
        => PartitionMovementParentCapacityParentRecords.Bound(value);
    internal static long Bound(PartitionMovePhaseResult value)
        => PartitionMovementParentCapacityOutcomeRecords.Bound(value);
    internal static long Bound(PartitionControlDelegation value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(ImmutableArray<IndexDefinition> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(ImmutableArray<RelationalColumn> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(ImmutableArray<VectorFieldProfile> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(PartitionMoveCleanupState value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(QueuePolicy value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(PartitionMoveResult value)
        => PartitionMovementParentCapacityOutcomeRecords.Bound(value);
    internal static long Bound(PartitionMoveReceiverSourceWitness value)
        => PartitionMovementParentCapacityReceiverRecords.Bound(value);
    internal static long Bound(SensitiveFieldPolicy value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(PartitionMoveRetireCancellationAttempt value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(PartitionMoveImageFamily value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(OperationResult value)
        => PartitionMovementParentCapacityOutcomeRecords.Bound(value);
    internal static long Bound(PartitionMovementReceiverIssuanceResult value)
        => PartitionMovementParentCapacityReceiverRecords.Bound(value);
    internal static long Bound(MutationReceipt value)
        => PartitionMovementParentCapacityOutcomeRecords.Bound(value);
    internal static long Bound(PartitionMoveRetireCancellationWitness value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(EventRetentionPolicy value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(PartitionMoveCheckpointBody value)
        => PartitionMovementParentCapacityParentRecords.Bound(value);
    internal static long Bound(PartitionMovePeerEnvelope value)
        => PartitionMovementParentCapacityTransportRecords.Bound(value);
    internal static long Bound(ResourceDefinition value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(PrincipalRecord value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(ImmutableArray<PartitionMoveImageFamily> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(ImmutableArray<ScopeGrant> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(PartitionMoveRetireCancellationDisposition value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(PartitionMovementReceiverIssueRequest value)
        => PartitionMovementParentCapacityReceiverRecords.Bound(value);
    internal static long Bound(PartitionMoveSourceFenceRecord value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(PartitionMovementTransportReply value)
        => PartitionMovementParentCapacityTransportRecords.Bound(value);
    internal static long Bound(VectorSpace value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(ReplicatedOperation value)
        => PartitionMovementParentCapacityTransportRecords.Bound(value);
    internal static long Bound(PartitionMoveAuthenticatedOutcomeWitness value)
        => PartitionMovementParentCapacityTransportRecords.Bound(value);
    internal static long Bound(PartitionMoveParentCancellation value)
        => PartitionMovementParentCapacityParentRecords.Bound(value);
    internal static long Bound(GrainOperationReply value)
        => PartitionMovementParentCapacityTransportRecords.Bound(value);
    internal static long Bound(CommitToken value)
        => PartitionMovementParentCapacityOutcomeRecords.Bound(value);
    internal static long Bound(PhysicalOwnerDirectoryV1 value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(PartitionControlEffectPayload value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(PartitionMoveReceiverIssuance value)
        => PartitionMovementParentCapacityReceiverRecords.Bound(value);
    internal static long Bound(PartitionMoveParentHeader value)
        => PartitionMovementParentCapacityParentRecords.Bound(value);
    internal static long Bound(PartitionMovePhaseCommand value)
        => PartitionMovementParentCapacityParentRecords.Bound(value);
    internal static long Bound(PartitionMoveParentState value)
        => PartitionMovementParentCapacityParentRecords.Bound(value);
    internal static long Bound(ImmutableArray<RegisteredPhysicalOwnerV1> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(ImmutableArray<SensitiveFieldPolicy> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(AtomicPartitionPlacementResolution value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(ReplicaSiloDiscovery value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(AtomicPartitionPlacementV1 value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(PartitionMoveJournalReceipt value)
        => PartitionMovementParentCapacityOutcomeRecords.Bound(value);
    internal static long Bound(PartitionMoveReceiverIssuanceWitness value)
        => PartitionMovementParentCapacityReceiverRecords.Bound(value);
    internal static long Bound(IndexDefinition value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(PartitionMoveReceiverIssuePacket value)
        => PartitionMovementParentCapacityReceiverRecords.Bound(value);
    internal static long Bound(PartitionMoveRequest value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(PartitionRef value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(CommitReceipt value)
        => PartitionMovementParentCapacityOutcomeRecords.Bound(value);
    internal static long Bound(PartitionControlCommandIdentity value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(RelationalSchema value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(PartitionMovePhaseGrant value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(PartitionControlCommandRecord value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(PartitionMoveExpiredRetireCancellation value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(PartitionMoveControlRecord value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(ImmutableArray<MutationReceipt> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(ImmutableArray<ResourceDefinition> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(ImmutableArray<string> values)
        => PartitionMovementParentCapacityCollections.Bound(values);
    internal static long Bound(PartitionMovementSourcePendingReply value)
        => PartitionMovementParentCapacityTransportRecords.Bound(value);
    internal static long Bound(PartitionMoveImageDescriptor value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(PartitionMoveReceiverEffectAdmission value)
        => PartitionMovementParentCapacityReceiverRecords.Bound(value);
    internal static long Bound(BlobPolicy value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(PartitionControlOutcomeReference value)
        => PartitionMovementParentCapacityAuthorityRecords.Bound(value);
    internal static long Bound(PartitionMoveCaptureWitness value)
        => PartitionMovementParentCapacityTransportRecords.Bound(value);
    internal static long Bound(PartitionMoveReceiverIssuanceSnapshot value)
        => PartitionMovementParentCapacityReceiverRecords.Bound(value);
    internal static long Bound(PhysicalShardRecord value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(RelationalColumn value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(ScopeGrant value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(VectorFieldProfile value)
        => PartitionMovementParentCapacityResourceRecords.Bound(value);
    internal static long Bound(RegisteredPhysicalOwnerV1 value)
        => PartitionMovementParentCapacityTopologyRecords.Bound(value);
    internal static long Bound(string? value)
        => PartitionMovementParentCapacityPrimitives.Bound(value);
}
