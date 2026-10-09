using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>One closed linked observation gate borrows the original issued parent request claim.</summary>
internal sealed class RequestCqrsReceiverIssueAdjunct(RequestCqrsProbeFiles files, RequestCqrsProbeLifecycle lifecycle,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> createMarker,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, IGrainContext?, CancellationToken, Task> hold)
{
    private const int PairMatchCeiling = 2;
    private const int ExactlyOneAdjunct = 1;
    private const int NoAdjunct = 0;
    private const int FirstAdjunct = 0;

    internal async Task<bool> TryObserveAsync(RequestCqrsProbeClaim primary, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        if (phase != GrainRequestPhase.ParentReceiverIssueObserved
            || primary.Arm.Record.Phase != RequestCqrsProbePhase.ParentReceiverIssueAcknowledged)
        { return false; }
        var snapshot = files.ReadSnapshot();
        var linked = snapshot.Arms.Where(arm => arm.Record.Phase == RequestCqrsProbePhase.ParentReceiverIssueObserved
            && arm.Record.SourceArmId is not null
            && (arm.Record.SourceArmId == primary.Arm.Record.ArmId
                || arm.Record.SourceRequestId == primary.Identity.RequestId
                || arm.Record.PrincipalId == primary.Identity.PrincipalId
                    && arm.Record.CommandId == primary.Identity.CommandId)).Take(PairMatchCeiling).ToArray();
        if (linked.Length == NoAdjunct) { return false; }
        if (linked.Length != ExactlyOneAdjunct || primary.ReceiverIssueAdjunct is not null)
        { throw Invalid(); }
        var arm = linked[FirstAdjunct];
        RequirePair(primary, arm, snapshot);
        var claim = new RequestCqrsProbeClaim(arm, primary.Identity);
        primary.ReceiverIssueAdjunct = claim;
        files.RequireActiveArm(arm);
        lifecycle.EnterGate();
        try
        {
            var marker = createMarker(claim, RequestCqrsProbePhase.ParentReceiverIssueObserved,
                RequestCqrsProbeOutcome.Observed);
            files.WriteMarker(marker);
            RequestCqrsProbeActivationCapture.Observe(files, marker, context);
            await hold(claim, RequestCqrsProbePhase.ParentReceiverIssueObserved, context,
                cancellationToken).ConfigureAwait(true);
            return true;
        }
        finally { lifecycle.ExitGate(); }
    }

    internal void ProducerDisposed(RequestCqrsProbeClaim primary)
    {
        if (primary.ReceiverIssueAdjunct is not { } adjunct) { return; }
        files.WriteClaimedProducerDisposed(createMarker(adjunct, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed), adjunct.Arm);
    }

    internal static void RequireDeclaredPair(RequestCqrsProbeArmRecord target,
        IReadOnlyList<RequestCqrsProbeLoadedArm> arms, IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        if (target.Phase != RequestCqrsProbePhase.ParentReceiverIssueObserved || target.SourceArmId is null)
        { return; }
        var primary = arms.SingleOrDefault(arm => arm.Record.ArmId == target.SourceArmId)?.Record;
        if (primary is not { } source || source.Phase != RequestCqrsProbePhase.ParentReceiverIssueAcknowledged
            || source.Action != RequestCqrsProbeAction.Hold || source.PrincipalId != target.PrincipalId
            || source.CommandId != target.CommandId || source.ReadKind is not null
            || source.SessionId != target.SessionId
            || !markers.Any(marker => marker.ArmId == source.ArmId && marker.CommandId == source.CommandId
                && marker.RequestId == target.SourceRequestId && marker.Phase == source.Phase
                && marker.Outcome == RequestCqrsProbeOutcome.Observed)
            || arms.Count(arm => arm.Record.Phase == RequestCqrsProbePhase.ParentReceiverIssueObserved
                && arm.Record.SourceArmId == source.ArmId) != ExactlyOneAdjunct)
        { throw Invalid(); }
    }

    private static void RequirePair(RequestCqrsProbeClaim primary, RequestCqrsProbeLoadedArm adjunct,
        RequestCqrsProbeSnapshot snapshot)
    {
        var source = primary.Arm.Record;
        var target = adjunct.Record;
        if (source.Action != RequestCqrsProbeAction.Hold || source.ReadKind is not null
            || primary.Identity.CommandId != source.CommandId || primary.Identity.PrincipalId != source.PrincipalId
            || primary.Identity.ReadKind is not null || primary.Identity.CommandKind != OperationKind.MovePartition
            || target.PrincipalId != source.PrincipalId || target.CommandId != source.CommandId
            || target.SourceRequestId != primary.Identity.RequestId || target.SourceArmId != source.ArmId
            || target.ReadKind is not null || target.Action != RequestCqrsProbeAction.Hold
            || target.Partition is not null || target.TargetVoter is not null
            || target.SessionId != source.SessionId || !snapshot.Arms.Any(arm => arm.Record.ArmId == source.ArmId
                && arm.ExactBytes.AsSpan().SequenceEqual(primary.Arm.ExactBytes)))
        { throw Invalid(); }
        RequirePrimaryMarker(primary, snapshot, RequestCqrsProbeOutcome.Observed);
        RequirePrimaryMarker(primary, snapshot, RequestCqrsProbeOutcome.Released);
    }

    private static void RequirePrimaryMarker(RequestCqrsProbeClaim primary, RequestCqrsProbeSnapshot snapshot,
        RequestCqrsProbeOutcome outcome)
    {
        var markers = snapshot.Markers.Where(marker => marker.ArmId == primary.Arm.Record.ArmId
            && marker.Phase == RequestCqrsProbePhase.ParentReceiverIssueAcknowledged && marker.Outcome == outcome)
            .Take(PairMatchCeiling).ToArray();
        if (markers.Length != ExactlyOneAdjunct || markers[FirstAdjunct].RequestId != primary.Identity.RequestId
            || markers[FirstAdjunct].CommandId != primary.Identity.CommandId)
        { throw Invalid(); }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
