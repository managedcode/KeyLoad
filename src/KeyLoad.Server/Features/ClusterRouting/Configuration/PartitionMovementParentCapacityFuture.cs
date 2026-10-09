using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Orleans;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacitySchema;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityFuture
{
    internal static long DigestField => Bytes(SHA256.HashSizeInBytes * HexadecimalCharactersPerHashByte);
    internal static long SiloField => Bytes(ReplicaTransportProtocol.MaximumAddressCharacters * MaximumUtf8BytesPerSiloCharacter);

    internal static long Root<T>(long value)
        => checked(ReferenceFieldBytes + ScalarFieldBytes + NativeEncodedTypeMeasure.Measure<T>() + value);

    internal static long FutureJournal(long owner)
        => checked(ReferenceFieldBytes + GuidFieldBytes + owner + ScalarFieldBytes + DigestField + DigestField);

    internal static long FutureDiscovery(PhysicalShardRecord owner, string cluster)
    {
        var voter = ReferenceFieldBytes;
        foreach (var value in owner.VoterIds)
        { voter = Math.Max(voter, PartitionMovementParentCapacityBounds.Bound(value)); }
        return checked(ReferenceFieldBytes + voter + PartitionMovementParentCapacityBounds.Bound(cluster) + GuidFieldBytes + SiloField
            + ScalarFieldBytes * FutureDiscoveryScalarOccurrences4);
    }

    internal static long FutureGrant(PartitionMoveParentState state, PhysicalShardRecord receiver,
        ImmutableArray<ResourceDefinition> resources)
        => checked(ReferenceFieldBytes + ScalarFieldBytes * FutureGrantScalarOccurrences8 + GuidFieldBytes * FutureGrantGuidOccurrences5
            + PartitionMovementParentCapacityBounds.Bound(state.Header!.OriginalTransferRequest.Partition) + PartitionMovementParentCapacityBounds.Bound(state.Header.ControlOwner)
            + PartitionMovementParentCapacityBounds.Bound(receiver) + PartitionMovementParentCapacityBounds.Bound(state.Header.OperatorPrincipalId) + DigestField * FutureGrantDigestOccurrences2
            + DateTimeFieldBytes + FutureJournal(PartitionMovementParentCapacityBounds.Bound(receiver)) * FutureGrantJournalOccurrences2 + PartitionMovementParentCapacityBounds.Bound(resources)
            + FutureRetireDisposition(state, receiver));

    internal static long FutureRetireDisposition(PartitionMoveParentState state, PhysicalShardRecord receiver)
        => checked(ReferenceFieldBytes + ScalarFieldBytes * FutureRetireDispositionScalarOccurrences3 + GuidFieldBytes * FutureRetireDispositionGuidOccurrences3
            + DigestField + DateTimeFieldBytes + FutureJournal(PartitionMovementParentCapacityBounds.Bound(receiver))
            + PartitionMovementParentCapacityBounds.Bound(state.Header!.OperatorPrincipalId));

    internal static long FutureEnvelope(PartitionMovePhaseCommand phase,
        long grant, long receiverWitness, long sourceWitness)
        => checked(ReferenceFieldBytes + ScalarFieldBytes * FutureEnvelopeScalarOccurrences3 + GuidFieldBytes * FutureEnvelopeGuidOccurrences3
            + PartitionMovementParentCapacityBounds.Bound(phase.Partition) + PartitionMovementParentCapacityBounds.Bound(phase.ControlOwner) + PartitionMovementParentCapacityBounds.Bound(phase.SourcePlacement)
            + PartitionMovementParentCapacityBounds.Bound(phase.DestinationOwner) + PartitionMovementParentCapacityBounds.Bound(phase.ControlIntentDigest) + DateTimeFieldBytes
            + Bytes(phase.Body.Length) + grant + receiverWitness + sourceWitness);

    internal static long FutureWitness(long reply)
        => checked(ReferenceFieldBytes + ScalarFieldBytes + GuidFieldBytes * FutureWitnessGuidOccurrences2 + Bytes(reply) + DigestField);

    internal static long GrainReply<T>(long typedValue)
    {
        var value = checked(ReferenceFieldBytes + NativeEncodedTypeMeasure.Measure<T>() + typedValue);
        return checked(ReferenceFieldBytes + Bytes(Root<GrainValue>(value)) + ReferenceFieldBytes * GrainReplyReferenceOccurrences2);
    }

    internal static long SourceReply(PartitionMoveParentState state, long pending, string cluster)
    {
        // The native reader aliases identical pending/last/selected IDs. Internal fields remain independent.
        var snapshot = checked(PartitionMovementParentCapacityBounds.Bound(state) + pending + ReferenceFieldBytes * SourceReplyReferenceOccurrences2);
        var result = checked(ReferenceFieldBytes + snapshot + ScalarFieldBytes * SourceReplyScalarOccurrences2);
        var reply = checked(ReferenceFieldBytes + ScalarFieldBytes * SourceReplyScalarOccurrences2 + GuidFieldBytes * SourceReplyGuidOccurrences2
            + DateTimeFieldBytes + PartitionMovementParentCapacityBounds.Bound(state.Header!.ControlOwner) + PartitionMovementParentCapacityBounds.Bound(state.CurrentOperatorPrincipalId)
            + GrainReply<PartitionMovementParentStateResult>(result)
            + FutureDiscovery(state.Header.ControlOwner, cluster));
        return Root<PartitionMovementSourcePendingReply>(reply);
    }

    internal static long TransportReply<T>(PhysicalShardRecord owner, string cluster, long value)
        => Root<PartitionMovementTransportReply>(checked(ReferenceFieldBytes + GuidFieldBytes * TransportReplyGuidOccurrences2
            + PartitionMovementParentCapacityBounds.Bound(owner) + FutureDiscovery(owner, cluster) + GrainReply<T>(value) + DigestField));

    internal static long FutureIssuance(PartitionMoveParentState state, PhysicalShardRecord receiver, long journal)
        => checked(ReferenceFieldBytes + ScalarFieldBytes * FutureIssuanceScalarOccurrences6 + GuidFieldBytes * FutureIssuanceGuidOccurrences5
            + PartitionMovementParentCapacityBounds.Bound(state.Header!.OriginalTransferRequest.Partition) + DigestField * FutureIssuanceDigestOccurrences4
            + PartitionMovementParentCapacityBounds.Bound(state.Header.OperatorPrincipalId) + PartitionMovementParentCapacityBounds.Bound(PartitionStoreProtocol.AdministratorId)
            + DateTimeFieldBytes + PartitionMovementParentCapacityBounds.Bound(receiver) + journal);

    internal static long FuturePhaseResult(PartitionMoveParentState state, long journal, long grant,
        long issuance, long cancellation)
        => checked(ReferenceFieldBytes + GuidFieldBytes + ScalarFieldBytes + journal
            + FutureControl(state) + FutureFence(state) + FutureCommit(state)
            + FuturePlacement(state) + grant + FutureCleanup(state, journal)
            + ReferenceFieldBytes * FuturePhaseResultReferenceOccurrences2 + issuance + cancellation);

    // This future reserve describes successful typed receipts only. Existing failed outcomes
    // are charged by PartitionMovementParentCapacityBounds.Bound(OperationResult); unbounded future native SafeDetail remains an open gate.
    // Actual observed/checkpoint limits continue to reject overflow without losing pending authority.
    internal static long FutureOutcome(long phaseResult)
        => checked(ReferenceFieldBytes * FutureOutcomeReferenceOccurrences4 + NativeEncodedTypeMeasure.Measure<PartitionMovePhaseResult>() + phaseResult);

    internal static long FutureCommit(PartitionMoveParentState state)
        => checked(ReferenceFieldBytes + GuidFieldBytes + ReferenceFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(state.Header!.OriginalTransferRequest.Partition.AtomicPartitionId) + ScalarFieldBytes * FutureCommitScalarOccurrences3
            + ReferenceFieldBytes * FutureCommitReferenceOccurrences2 + ScalarFieldBytes);

    internal static long FutureFence(PartitionMoveParentState state)
        => checked(ReferenceFieldBytes + ScalarFieldBytes * FutureFenceScalarOccurrences2 + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(state.Header!.OriginalTransferRequest.Partition) + PartitionMovementParentCapacityBounds.Bound(state.Header.ControlOwner)
            + PartitionMovementParentCapacityBounds.Bound(state.Header.SourcePlacement) + PartitionMovementParentCapacityBounds.Bound(state.Header.DestinationOwner) + DigestField);

    internal static long FutureCleanup(PartitionMoveParentState state, long journal)
        => checked(ReferenceFieldBytes + ScalarFieldBytes * FutureCleanupScalarOccurrences5 + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(state.Header!.OriginalTransferRequest.Partition) + DigestField + journal);

    internal static long FutureDescriptor(PartitionMoveParentState state, ImmutableArray<ResourceDefinition> resources)
    {
        var families = checked(ReferenceFieldBytes * FutureDescriptorReferenceOccurrences2 + ScalarFieldBytes);
        foreach (var family in PartitionRecordFamilies.All)
        { families = checked(families + ReferenceFieldBytes + PartitionMovementParentCapacityBounds.Bound(family) + ScalarFieldBytes * FutureDescriptorScalarOccurrences3 + DigestField); }
        return checked(ReferenceFieldBytes + ScalarFieldBytes * FutureDescriptorScalarOccurrences2 + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(state.Header!.OriginalTransferRequest.Partition) + PartitionMovementParentCapacityBounds.Bound(state.Header.SourcePlacement)
            + families + DigestField + PartitionMovementParentCapacityBounds.Bound(resources));
    }

    internal static long FuturePlacement(PartitionMoveParentState state)
        => checked(ReferenceFieldBytes + ScalarFieldBytes * FuturePlacementScalarOccurrences3 + GuidFieldBytes * FuturePlacementGuidOccurrences2
            + PartitionMovementParentCapacityBounds.Bound(state.Header!.OriginalTransferRequest.Partition) + PartitionMovementParentCapacityBounds.Bound(state.Header.DestinationOwner.VoterIds));

    internal static long FutureControl(PartitionMoveParentState state)
        => checked(PartitionMovementParentCapacityBounds.Bound(state.Control!) + DigestField + FutureCommit(state) + FuturePlacement(state));

    internal static long FuturePending(PartitionMovePhaseCommand phase,
        PhysicalShardRecord receiver, long grant, long journal)
        => checked(ReferenceFieldBytes + ScalarFieldBytes * FuturePendingScalarOccurrences5 + GuidFieldBytes * FuturePendingGuidOccurrences4
            + PartitionMovementParentCapacityBounds.Bound(phase.Partition) + DigestField * FuturePendingDigestOccurrences2 + PartitionMovementParentCapacityBounds.Bound(phase) + grant + GuidFieldBytes
            + journal * FuturePendingJournalOccurrences8 + PartitionMovementParentCapacityBounds.Bound(receiver) + DateTimeFieldBytes + ReferenceFieldBytes * FuturePendingReferenceOccurrences12);

    internal static long CaptureFields(PartitionMoveParentState state, PhysicalShardRecord receiver,
        ImmutableArray<ResourceDefinition> resources, string cluster)
    {
        var descriptor = FutureDescriptor(state, resources);
        var handle = checked(ReferenceFieldBytes + GuidFieldBytes + DateTimeFieldBytes
            + descriptor + ScalarFieldBytes);
        var witness = FutureWitness(TransportReply<PartitionMovementCaptureHandle>(receiver, cluster, handle));
        return checked(descriptor + FutureFence(state) + witness + FutureJournal(PartitionMovementParentCapacityBounds.Bound(receiver)));
    }

    internal static long CancellationFields(PartitionMoveParentState state, PartitionMovePhaseCommand phase,
        PhysicalShardRecord receiver, string cluster, long grant, long journal, long freshSource)
    {
        var envelope = FutureEnvelope(phase, grant, ReferenceFieldBytes, ReferenceFieldBytes);
        var request = Root<PartitionMovementRetireCancellationRequest>(checked(ReferenceFieldBytes
            + ScalarFieldBytes * CancellationFieldsScalarOccurrences2 + GuidFieldBytes * CancellationFieldsGuidOccurrences3 + envelope + journal + freshSource
            + DateTimeFieldBytes + FutureDiscovery(state.Header!.ControlOwner, cluster) + SiloField));
        var attempt = checked(ReferenceFieldBytes + ScalarFieldBytes + GuidFieldBytes * CancellationFieldsGuidOccurrences2
            + DateTimeFieldBytes + Bytes(request) + DigestField);
        var body = Root<PartitionMoveRetireCancellationBody>(checked(ReferenceFieldBytes
            + ScalarFieldBytes * CancellationFieldsScalarOccurrences3 + GuidFieldBytes * CancellationFieldsGuidOccurrences2 + envelope + journal + Bytes(request)
            + DigestField + DateTimeFieldBytes + PartitionMovementParentCapacityBounds.Bound(PartitionStoreProtocol.AdministratorId) + freshSource));
        var cancellationPhase = checked(PartitionMovementParentCapacityBounds.Bound(phase) + Bytes(body));
        var cancellation = checked(ReferenceFieldBytes + ScalarFieldBytes * CancellationFieldsScalarOccurrences5 + GuidFieldBytes * CancellationFieldsGuidOccurrences3
            + DigestField + DateTimeFieldBytes + journal + PartitionMovementParentCapacityBounds.Bound(PartitionStoreProtocol.AdministratorId)
            + cancellationPhase);
        var result = FuturePhaseResult(state, journal, ReferenceFieldBytes, ReferenceFieldBytes, cancellation);
        var snapshot = checked(ReferenceFieldBytes + cancellation + FutureOutcome(result) + ScalarFieldBytes);
        var query = checked(ReferenceFieldBytes + snapshot + ScalarFieldBytes * CancellationFieldsScalarOccurrences2);
        var reply = TransportReply<PartitionMovementRetireCancellationOutcomeResult>(receiver, cluster, query);
        var witness = checked(ReferenceFieldBytes + cancellation + Bytes(reply) + DigestField);
        return checked(attempt + witness);
    }

    internal static long NativeOperation(PartitionMoveParentState state, long command)
    {
        var principal = Math.Max(PartitionMovementParentCapacityBounds.Bound(state.Header!.OperatorPrincipalId), PartitionMovementParentCapacityBounds.Bound(PartitionStoreProtocol.AdministratorId));
        var authority = Root<NativeCommandAuthority>(checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(NativeAuthorityContract.Purpose) + GuidFieldBytes * NativeOperationGuidOccurrences2 + ScalarFieldBytes
            + principal + DigestField + Bytes(SHA256.HashSizeInBytes) + ReferenceFieldBytes * NativeOperationReferenceOccurrences2));
        var payload = Root<NativeCommandPayload>(checked(ReferenceFieldBytes + Bytes(command)
            + ReferenceFieldBytes * NativeOperationReferenceOccurrences2 + Bytes(authority) + Bytes(SHA256.HashSizeInBytes)));
        return Root<ReplicatedOperation>(checked(ReferenceFieldBytes + GuidFieldBytes + ScalarFieldBytes
            + principal + DateTimeFieldBytes + PartitionMovementParentCapacityBounds.Bound(string.Empty) + Bytes(payload)));
    }
}
