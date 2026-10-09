using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityFuture;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacitySchema;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityAdmission
{
    internal static void RequireReceiverIssueCapacity(PartitionMoveReceiverIssueCapacityContext context,
        string actualClusterId)
    {
        var header = context.ActualState.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (context.Version != PartitionMoveProtocol.Version || context.MaxBatchBytes <= PartitionMovementProtocol.NoResultBytes
            || header.RetainedPhaseCount >= context.MaxPhaseRecordsPerMove)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var pending = RequireFutureCapacity(context.ActualState, context.IntendedPhase,
            context.CandidateGrant.ReceiverOwner, context.CandidateGrant.Resources,
            actualClusterId, context.MaxBatchBytes);
        if (pending > context.MaxRetainedMetadataBytesPerMove - header.RetainedMetadataBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
    }

    internal static void RequirePlannedStageCapacity(PartitionMoveParentState state,
        PartitionMovePhaseCommand phase, PhysicalShardRecord receiver, ImmutableArray<ResourceDefinition> resources,
        string actualClusterId, long maximumBytes)
        => _ = RequireFutureCapacity(state, phase, receiver, resources, actualClusterId, maximumBytes);

    private static long RequireFutureCapacity(PartitionMoveParentState state, PartitionMovePhaseCommand phase,
        PhysicalShardRecord receiver, ImmutableArray<ResourceDefinition> resources, string cluster, long maximumBytes)
    {
        if (state.Header is null || state.Control is null || resources.IsDefault
            || string.IsNullOrWhiteSpace(cluster) || maximumBytes <= PartitionMovementProtocol.NoResultBytes)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        try
        { return FutureCapacity(state, phase, receiver, resources, cluster, maximumBytes); }
        catch (OverflowException)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
    }

    private static long FutureCapacity(PartitionMoveParentState state, PartitionMovePhaseCommand phase,
        PhysicalShardRecord receiver, ImmutableArray<ResourceDefinition> resources, string cluster, long maximumBytes)
    {
        var grant = FutureGrant(state, receiver, resources);
        var journal = FutureJournal(Math.Max(PartitionMovementParentCapacityBounds.Bound(receiver), PartitionMovementParentCapacityBounds.Bound(state.Header!.ControlOwner)));
        var pending = checked(FuturePending(phase, receiver, grant, journal) + PartitionMovementParentCapacityBounds.Bound(resources));
        var stateCapacity = PartitionMovementParentCapacityFinalInstall.StateCapacity(state, phase, receiver, resources, grant, journal);
        var pendingSlots = PartitionMovementParentCapacityFinalInstall.IsFinal(state, phase)
            ? PartitionMovementFinalOutcomeCapacityProtocol.ParentPhaseSlots : PartitionMovementFinalOutcomeCapacityProtocol.SinglePhaseSlot;
        var source = FutureWitness(SourceReply(state, pending, cluster, stateCapacity, pendingSlots));
        var envelope = FutureEnvelope(phase, grant, ReferenceFieldBytes, ReferenceFieldBytes);
        var request = Root<PartitionMovementReceiverIssueRequest>(checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes * FutureCapacityGuidOccurrences3 + envelope + source + journal * FutureCapacityJournalOccurrences2
            + FutureDiscovery(state.Header.ControlOwner, cluster) + SiloField));
        var issuerBody = Root<PartitionMoveReceiverIssueBody>(checked(ReferenceFieldBytes + ScalarFieldBytes * FutureCapacityScalarOccurrences2
            + GuidFieldBytes + envelope + journal + Bytes(source) + DigestField
            + Bytes(request) + DigestField + PartitionMovementParentCapacityBounds.Bound(PartitionStoreProtocol.AdministratorId)));
        var issuerCommand = Root<PartitionMovePhaseCommand>(checked(PartitionMovementParentCapacityBounds.Bound(phase) + Bytes(issuerBody)));
        var packet = FutureWitness(request);
        var issued = FutureIssuance(state, receiver, journal);
        var issuedResult = FuturePhaseResult(state, journal, ReferenceFieldBytes, issued, ReferenceFieldBytes);
        var snapshot = checked(ReferenceFieldBytes + issued + FutureOutcome(issuedResult) + ScalarFieldBytes);
        var query = checked(ReferenceFieldBytes + snapshot + ScalarFieldBytes * FutureCapacityScalarOccurrences2);
        var proof = FutureWitness(TransportReply<KeyLoad.Orleans.PartitionMovementReceiverIssuanceResult>(receiver, cluster, query));
        pending = checked(pending + source + packet + proof);
        if (phase.Stage == PartitionMovePeerStage.Capture)
        { pending = checked(pending + CaptureFields(state, receiver, resources, cluster)); }
        var dispatch = FutureWitness(SourceReply(state, pending, cluster, stateCapacity, pendingSlots));
        var finalEnvelope = FutureEnvelope(phase, grant, proof, dispatch);
        var transport = Root<PartitionMovementTransportRequest>(checked(ReferenceFieldBytes + GuidFieldBytes * FutureCapacityGuidOccurrences2
            + finalEnvelope + journal + FutureDiscovery(state.Header.ControlOwner, cluster) + ScalarFieldBytes * FutureCapacityScalarOccurrences2));
        var admission = checked(ReferenceFieldBytes + ScalarFieldBytes + GuidFieldBytes + DateTimeFieldBytes
            + grant + proof + dispatch);
        var effectCommand = Root<PartitionMovePhaseCommand>(checked(PartitionMovementParentCapacityBounds.Bound(phase) + PartitionMovementParentCapacityBounds.Bound(resources)
            + GuidFieldBytes + admission));
        var outcome = PartitionMovementParentCapacityFinalInstall.OutcomeCapacity(state, phase,
            FutureOutcome(FuturePhaseResult(state, journal, grant, issued, ReferenceFieldBytes)));
        var nativeOutcomeWitness = FutureOutcomeWitness(outcome, ReferenceFieldBytes);
        var outcomeReply = TransportReply<KeyLoad.Orleans.PartitionMovementOutcomeWitness>(receiver, cluster, nativeOutcomeWitness);
        var outcomeProof = FutureOutcomeProof(outcomeReply);
        var completedOutcomeWitness = Root<KeyLoad.Orleans.PartitionMovementOutcomeWitness>(FutureOutcomeWitness(outcome, outcomeProof));
        var retained = checked(pending + outcome + outcomeProof);
        if (phase.Stage == PartitionMovePeerStage.Retire)
        { retained = checked(retained + CancellationFields(state, phase, receiver, cluster, grant, journal, dispatch)); }
        var parent = Root<PartitionMoveParentPhase>(retained);
        var checkpoint = Root<PartitionMoveCheckpointBody>(checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(state.Header.OriginalTransferRequest) + PartitionMovementParentCapacityBounds.Bound(state.Header.OperatorPrincipalId)
            + ScalarFieldBytes * FutureCapacityScalarOccurrences4 + GuidFieldBytes * FutureCapacityGuidOccurrences7 + DateTimeFieldBytes * FutureCapacityDateTimeOccurrences2 + retained));
        var checkpointCommand = Root<PartitionMovePhaseCommand>(checked(PartitionMovementParentCapacityBounds.Bound(phase) + Bytes(checkpoint)));
        var observedSource = SourceReply(state, retained, cluster, checked(stateCapacity + outcome), pendingSlots);
        RequireBound(maximumBytes, PartitionMovementParentCapacityFinalInstall.IsFinal(state, phase)
            ? PartitionMovementFinalOutcomeCapacityProtocol.Exceeded : PartitionMoveProtocol.Capacity,
            request, proof, dispatch, transport, outcomeReply,
            completedOutcomeWitness, observedSource, parent, checkpoint,
            NativeOperation(state, issuerCommand), NativeOperation(state, effectCommand),
            NativeOperation(state, checkpointCommand));
        return parent;
    }

    private static void RequireBound(long maximum, string detail, params long[] values)
    {
        foreach (var value in values)
        {
            if (value > maximum)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, detail); }
        }
    }
}
