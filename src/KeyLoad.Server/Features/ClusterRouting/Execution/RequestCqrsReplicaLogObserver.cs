using System.Runtime.ExceptionServices;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsReplicaLogObserver(RequestCqrsProbeFiles files, RequestCqrsProbeLifecycle lifecycle,
    string voter, string siloAddress, Func<RequestCqrsProbeArmRecord, ReplicaEntry, RequestCqrsProbePhase, RequestCqrsProbeMarkerRecord> createMarker,
    Action throwOrdinary)
{
    private const int SingleMatch = 1;
    private const int MatchCeiling = 2;
    private const int NoMatches = 0;
    private const int FirstMatch = 0;

    internal void Observe(ReplicaEntry entry)
    {
        lifecycle.EnterCallback();
        RequestCqrsProbeLoadedArm? matched = null;
        Exception? primary = null;
        Exception? cleanup = null;
        try
        { ObserveCore(entry, arm => matched = arm); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { primary = error; }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { primary = error; }
        try
        {
            lifecycle.ExitCallback();
            if (matched is not null)
            { files.WriteMarker(createMarker(matched.Record, entry, RequestCqrsProbePhase.ReplicaEntryObserverDisposed)); }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup = error; }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup = error; }
        if (primary is not null && cleanup is not null)
        { throw new AggregateException(primary, cleanup); }
        if (primary is not null)
        { ExceptionDispatchInfo.Capture(primary).Throw(); }
        if (cleanup is not null)
        { ExceptionDispatchInfo.Capture(cleanup).Throw(); }
    }

    private void ObserveCore(ReplicaEntry entry, Action<RequestCqrsProbeLoadedArm> matched)
    {
        if (entry.Operation is not { Kind: OperationKind.Batch } operation)
        { return; }
        var command = NativeCommandPayload.Read<CommandRequest>(operation);
        var snapshot = files.ReadSnapshot();
        var arms = snapshot.Arms.Where(arm => arm.Record.Phase == RequestCqrsProbePhase.ReplicaEntryAcknowledged
            && arm.Record.TargetVoter == voter && arm.Record.PrincipalId == operation.PrincipalId
            && arm.Record.CommandId == operation.Id && command.CommandId == operation.Id
            && arm.Record.Partition?.ToPartition() == command.Partition
            && !snapshot.Markers.Any(marker => marker.ArmId == arm.Record.ArmId)).Take(MatchCeiling).ToArray();
        if (arms.Length == NoMatches)
        { return; }
        if (arms.Length != SingleMatch)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidFiles); }
        var arm = arms[FirstMatch];
        RequestCqrsReplicaLogSourceMarkerValidation.Require(snapshot, arm.Record, siloAddress);
        files.RequireActiveArm(arm);
        matched(arm);
        files.WriteMarker(createMarker(arm.Record, entry, RequestCqrsProbePhase.ReplicaEntryAcknowledged));
        throwOrdinary();
    }
}
