using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsMovementJournalCapture
{
    internal static void CaptureReturned(RequestCqrsProbeLifecycle lifecycle, RequestCqrsProbeFiles files,
        global::KeyLoad.Orleans.GrainRequestProbeIdentity identity, OperationResult actual,
        global::Orleans.Runtime.IGrainContext context, string voter, string siloAddress)
    {
        var claim = lifecycle.FindClaim(identity.RequestId);
        if (claim?.Arm.Record.Partition is null || claim.Arm.Record.Phase != RequestCqrsProbePhase.SubmitReturned)
        { return; }
        ArgumentNullException.ThrowIfNull(context);
        lifecycle.EnterCallback();
        try
        {
            files.RequireActiveArm(claim.Arm);
            if (claim.Identity != identity || claim.MovementJournal is not null
                || global::Orleans.Runtime.RequestContext.Get(global::KeyLoad.Orleans.GrainRequestStreamProtocol.ContextKey)
                    is not global::KeyLoad.Orleans.GrainRequestContextState request
                || request.RequestId != identity.RequestId || request.CommandId != identity.CommandId
                || request.ConnectionId != claim.ConnectionId)
            { throw Invalid(); }
            claim.MovementJournal = Capture(claim, actual, voter, siloAddress);
        }
        finally
        { lifecycle.ExitCallback(); }
    }

    internal static RequestCqrsMovementJournalRecord Capture(RequestCqrsProbeClaim claim,
        OperationResult actual, string voter, string siloAddress)
    {
        var arm = claim.Arm.Record;
        if (arm.Phase != RequestCqrsProbePhase.SubmitReturned || arm.Action != RequestCqrsProbeAction.Hold
            || arm.Partition is not { } scoped || arm.ReadKind is not null
            || claim.Identity.CommandKind != OperationKind.PartitionMovementPhase
            || arm.CommandId != claim.Identity.CommandId || arm.PrincipalId != claim.Identity.PrincipalId)
        { throw Invalid(); }
        var partition = scoped.ToPartition();
        if (actual.Error is { } error)
        { return Record(claim, voter, siloAddress, partition, Guid.Empty, null, null, error); }
        if (actual.NativeValue is not PartitionMovePhaseResult phase
            || !RequestCqrsMovementJournalInventory.IsObservedControlStage(phase.Stage) || phase.Control is not { } control
            || control.Partition != partition || control.MoveId != phase.MoveId
            || control.PrincipalId != arm.PrincipalId || phase.Journal.CommandId != arm.CommandId
            || !HasCommittedControlShape(phase))
        { throw Invalid(); }
        return Record(claim, voter, siloAddress, partition, phase.MoveId, phase.Stage, phase.Journal, null);
    }

    private static RequestCqrsMovementJournalRecord Record(RequestCqrsProbeClaim claim,
        string voter, string siloAddress, PartitionRef partition, Guid moveId,
        PartitionMovePeerStage? stage, PartitionMoveJournalReceipt? journal, ErrorCode? error)
        => new(RequestCqrsProbeProtocol.Version, RequestCqrsMovementJournalInventory.Kind,
            claim.Arm.Record.SessionId, claim.Arm.Record.ArmId, claim.Identity.RequestId,
            claim.Identity.CommandId, claim.ConnectionId, claim.Identity.PrincipalId!, voter, siloAddress,
            partition, moveId, stage, journal, error);

    internal static void PublishReturned(RequestCqrsProbeFiles files, RequestCqrsProbeClaim claim,
        RequestCqrsProbeMarkerRecord marker)
    {
        if (marker.Phase == RequestCqrsProbePhase.SubmitReturned && claim.MovementJournal is { } journal)
        {
            files.MovementJournals.Publish(journal, marker, claim);
            claim.MovementJournal = null;
        }
        else
        { files.WriteMarker(marker); }
    }

    private static bool HasCommittedControlShape(PartitionMovePhaseResult phase)
        => phase.Stage switch
        {
            PartitionMovePeerStage.ControlPrepare => phase.Control!.Phase == PartitionMovePhase.Prepared,
            PartitionMovePeerStage.ControlAcceptFence => phase.Control!.Phase == PartitionMovePhase.Fenced
                && phase.Fence is not null && phase.Control.SourceCut == phase.Fence.SourceCut,
            PartitionMovePeerStage.ControlFinalize => phase.Control!.Phase == PartitionMovePhase.Published
                && phase.Control.InstalledReceipt is not null && phase.Control.PublishedPlacement is not null
                && SamePublication(phase.PublishedPlacement, phase.Control.PublishedPlacement),
            _ => false
        };

    private static bool SamePublication(AtomicPartitionPlacementV1? actual, AtomicPartitionPlacementV1 expected)
        => actual is not null && actual.Version == expected.Version && actual.Partition == expected.Partition
            && actual.PhysicalShardId == expected.PhysicalShardId && actual.Revision == expected.Revision
            && actual.Incarnation == expected.Incarnation && actual.PlacementEpoch == expected.PlacementEpoch
            && actual.VoterIds.SequenceEqual(expected.VoterIds, StringComparer.Ordinal);

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
