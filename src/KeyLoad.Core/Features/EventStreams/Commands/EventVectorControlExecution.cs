using System.Collections.Immutable;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorControlResultVersion = 1;
    private const string InvalidEventVectorControlPlacement = "The original event vector control placement is inconsistent.";

    private OperationResult ExecuteEventVectorControl(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, long position, AtomicPartitionPlacementResolution? placement)
    {
        var current = Principal(transaction, principal.Id, EvaluationClock.GetUtcNow());
        var phase = Payload<EventVectorControlPhase>(operation);
        var actual = RequireEventVectorControlView(transaction, current, phase);
        RequireEventVectorControlPlacement(placement, actual);
        if (EventVectorFirstControlFields.IsIssuance(phase.Action))
        { return ExecuteEventVectorFirstIssuance(transaction, current, phase, operation, position, actual); }
        EventVectorParentCommit.RequirePrevious(transaction, current, phase, Store.Identity.Incarnation,
            Authorization, OperationLimitsOptions);
        if (!phase.OriginalCleanupFrontierBytes.IsEmpty)
        { EventVectorCleanupParentCommit.Prepare(transaction, current, phase, operation, Authorization, OperationLimitsOptions); }
        var retained = EventVectorMapInventory.Read(transaction, phase.OriginalRequest.ControlPartition,
            phase.OriginalRequest.MapId, OperationLimitsOptions, CancellationToken.None);
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var scopes = EventVectorParentScopes.Derive(phase, retained, admission);
        EventVectorParentScopes.Authorize(Authorization, current, scopes, admission);
        var raw = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span).Value;
        var first = EventVectorFirstOpenProof.RequireReserved(transaction, current, phase, operation, raw, Authorization, admission);
        if (phase.Action == EventVectorControlAction.Open)
        {
            var issuance = EventVectorFirstIssuanceValidation.Read(transaction,
                phase.OriginalRequest.ControlPartition, current.Id, phase.OriginalRequest.CommandId,
                terminal: false, admission);
            EventVectorFirstIssuanceWriter.TransferToMap(transaction, issuance, admission);
        }
        var value = ExecuteEventVectorControlValue(transaction, current, phase, operation);
        value = EventVectorParentCommit.Write(transaction, current, phase, operation, value, scopes,
            raw, first, Authorization, OperationLimitsOptions);
        var receipt = new CommitReceipt(operation.Id, new CommitToken(actual.Incarnation,
            actual.Partition.AtomicPartitionId, position, actual.PlacementEpoch),
            ImmutableArray<MutationReceipt>.Empty, Durability);
        var result = new EventVectorControlResult(EventVectorControlResultVersion, operation.Id,
            value as EventVectorMap, value as EventVectorOffer, value as EventVectorSourcePhase, receipt)
        { OriginalSourcePageBytes = phase.OriginalSourcePageBytes };
        return Result(RetainEventVectorPublicResult(phase, result, retained.Header, admission));
    }

    private object ExecuteEventVectorControlValue(IAtomicTransaction transaction, PrincipalRecord principal,
        EventVectorControlPhase phase, ReplicatedOperation operation)
        => phase.Action switch
        {
            EventVectorControlAction.Open => EventVectorControlOpen.Execute(transaction, principal, phase,
                Store.Identity.Incarnation, operation.EvaluatedAt.Add(eventSourceCursorLifetime),
                OperationLimitsOptions, CancellationToken.None),
            EventVectorControlAction.AdmitSource => EventVectorControlAdmit.Execute(transaction, phase,
                OperationLimitsOptions, CancellationToken.None),
            EventVectorControlAction.ObserveSource => EventVectorControlObserve.Execute(transaction, phase,
                OperationLimitsOptions, CancellationToken.None),
            EventVectorControlAction.PublishOpen => EventVectorControlPublishOpen.Execute(transaction, phase,
                OperationLimitsOptions, CancellationToken.None),
            EventVectorControlAction.Offer => EventVectorControlOffer.Execute(transaction, phase,
                operation.EvaluatedAt, OperationLimitsOptions, CancellationToken.None),
            EventVectorControlAction.PublishAcknowledgement => EventVectorControlAcknowledgement.Execute(
                transaction, phase, OperationLimitsOptions, CancellationToken.None),
            EventVectorControlAction.PublishRefresh => EventVectorControlRefresh.Execute(transaction, phase,
                OperationLimitsOptions, CancellationToken.None),
            EventVectorControlAction.CompleteRelease => EventVectorControlRelease.Execute(transaction, phase,
                OperationLimitsOptions, CancellationToken.None),
            _ => throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority)
        };

    private static void RequireEventVectorControlPlacement(AtomicPartitionPlacementResolution? supplied,
        AtomicPartitionPlacementResolution actual)
    {
        if (supplied is null || supplied.Version != actual.Version || supplied.Partition != actual.Partition
            || supplied.PhysicalShardId != actual.PhysicalShardId || supplied.Incarnation != actual.Incarnation
            || supplied.PlacementEpoch != actual.PlacementEpoch || supplied.DirectoryRevision != actual.DirectoryRevision
            || supplied.Revision != actual.Revision || supplied.IsFallback != actual.IsFallback
            || !supplied.VoterIds.SequenceEqual(actual.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, InvalidEventVectorControlPlacement); }
    }
}

