using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsCanonicalApplyObserver(RequestCqrsProbeFiles files,
    RequestCqrsProbeLifecycle lifecycle,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> createMarker,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, CancellationToken, Task> hold)
{
    private const int SingleMatch = 1;
    private const int MatchCeiling = 2;
    private const int NoMatchingArm = 0;
    private const int FirstMatchingArmIndex = 0;
    internal RequestCqrsCanonicalApplyScope? EnterCanonical(ReplicaEntry entry, PartitionRef partition, string voter,
        Action restore, Action<RequestCqrsCanonicalApplyScope, bool> holding)
    {
        var operation = entry.Operation!;
        var snapshot = files.ReadSnapshot();
        var arms = snapshot.Arms.Where(arm => arm.Record.Phase == RequestCqrsProbePhase.CanonicalJournalFlushed
            && arm.Record.TargetVoter == voter && arm.Record.PrincipalId == operation.PrincipalId
            && arm.Record.CommandId == operation.Id && arm.Record.Partition?.ToPartition() == partition)
            .Take(MatchCeiling).ToArray();
        if (arms.Length > SingleMatch)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidFiles); }
        if (arms.Length == NoMatchingArm)
        { return null; }
        var arm = arms[FirstMatchingArmIndex];
        RequestCqrsCanonicalSourceArmValidation.Require(snapshot, arm.Record);
        var identity = new GrainRequestProbeIdentity(arm.Record.SourceRequestId!.Value, operation.Id,
            operation.PrincipalId, null, OperationKind.Batch);
        var claim = new RequestCqrsProbeClaim(arm, identity) { EntryIndex = entry.Index, EntryTerm = entry.Term };
        files.RequireActiveArm(arm);
        lifecycle.EnterCallback();
        try
        { return new(this, claim, restore, holding); }
        catch (Exception primary)
        {
            try
            { lifecycle.ExitCallback(); }
            catch (Exception cleanup)
            { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
    internal void ExitCanonical(RequestCqrsProbeClaim claim)
    {
        try
        { files.WriteMarker(createMarker(claim, RequestCqrsProbePhase.CanonicalOwnerDisposed, RequestCqrsProbeOutcome.Observed), claim.Arm); }
        finally { lifecycle.ExitCallback(); }
    }
    internal void HoldCanonical(RequestCqrsProbeClaim claim)
    {
        files.RequireActiveArm(claim.Arm);
        lifecycle.EnterGate();
        try
        {
            files.WriteMarker(createMarker(claim, RequestCqrsProbePhase.CanonicalJournalFlushed, RequestCqrsProbeOutcome.Observed));
            hold(claim, RequestCqrsProbePhase.CanonicalJournalFlushed, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally { lifecycle.ExitGate(); }
    }
    internal void ObserveIndependentAppend(RequestCqrsProbeClaim claim)
        => files.WriteMarker(createMarker(claim, RequestCqrsProbePhase.CanonicalIndependentAppendCompleted,
            RequestCqrsProbeOutcome.Observed), claim.Arm);
    internal void ObserveCanonicalOutbound(RequestCqrsProbeClaim claim)
        => files.WriteMarker(createMarker(claim, RequestCqrsProbePhase.CanonicalOutboundObserved,
            RequestCqrsProbeOutcome.Observed), claim.Arm);
}
