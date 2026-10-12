using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>One actual signed status claim observes natural checkpoint publication without owning storage.</summary>
internal sealed class RequestCqrsReplicaSnapshotObserver(RequestCqrsProbeFiles files,
    RequestCqrsProbeLifecycle lifecycle, IOptions<ReplicaConfiguration> replicaOptions,
    IOptions<RequestProbeExecutionOptions> options, TimeProvider clock,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> marker,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, IGrainContext?, CancellationToken, Task> hold,
    CancellationToken stopping, CancellationToken applicationStopping)
{
    private const long ReservedTailCommands = 1;
    private const long NoSnapshot = 0;

    internal static RequestCqrsReplicaSnapshotObserver? Find(IServiceProvider? services)
        => (services?.GetService<IGrainRequestPhaseObserver>() as RequestCqrsProbeObserver)?.ReplicaSnapshot;

    internal async Task<ReplicaNodeState> ObserveAsync(ReplicaNodeState state,
        Func<CancellationToken, Task<ReplicaNodeState>> read, CancellationToken token)
    {
        if (global::Orleans.Runtime.RequestContext.Get(GrainRequestStreamProtocol.ContextKey)
            is not GrainRequestContextState context)
        { return state; }
        var claim = lifecycle.FindClaim(context.RequestId);
        if (claim is null || claim.Arm.Record.Phase != RequestCqrsProbePhase.ReplicaSnapshotReadiness)
        { return state; }
        RequireClaim(claim, context, state);
        lifecycle.EnterClaimedCallback();
        var gate = false;
        try
        {
            lifecycle.EnterGate();
            gate = true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping, applicationStopping);
            state = await read(linked.Token).ConfigureAwait(true);
            RequireClaim(claim, context, state);
            while (!Qualifies(state))
            {
                linked.Token.ThrowIfCancellationRequested();
                files.RequireActiveArm(claim.Arm);
                await Task.Delay(options.Value.PollInterval, clock, linked.Token).ConfigureAwait(true);
                state = await read(linked.Token).ConfigureAwait(true);
                RequireClaim(claim, context, state);
            }
            files.ReplicaSnapshot.Publish(claim, new(RequestCqrsProbeProtocol.Version,
                RequestCqrsReplicaSnapshotJson.Kind, files.SessionId, claim.Arm.Record.ArmId,
                claim.Identity.RequestId, state.SnapshotIndex, state.MaterializedPosition,
                state.CommittedIndex, state.LastIndex, state.HasCheckpointPrefix!.Value, state.CheckpointPending!.Value));
            files.WriteMarker(marker(claim, RequestCqrsProbePhase.ReplicaSnapshotReadiness, RequestCqrsProbeOutcome.Observed));
            await hold(claim, RequestCqrsProbePhase.ReplicaSnapshotReadiness, null, linked.Token).ConfigureAwait(true);
            return state;
        }
        finally
        {
            if (gate)
            { lifecycle.ExitGate(); }
            lifecycle.ExitCallback();
        }
    }

    private void RequireClaim(RequestCqrsProbeClaim claim, GrainRequestContextState context, ReplicaNodeState state)
    {
        if (context.RequestId != claim.Identity.RequestId || context.CommandId != Guid.Empty
            || context.ConnectionId == Guid.Empty || claim.Identity.CommandId != Guid.Empty
            || claim.Arm.Record.ReadKind != GrainReadKind.NodeStatus || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold
            || claim.Arm.Record.TargetVoter != replicaOptions.Value.LocalId || state.VoterId != replicaOptions.Value.LocalId)
        { throw RequestCqrsReplicaSnapshotJson.Invalid(); }
    }

    private bool Qualifies(ReplicaNodeState state) => state.SnapshotIndex > NoSnapshot
        && state.HasCheckpointPrefix is false && state.CheckpointPending is false
        && state.MaterializedPosition >= state.CommittedIndex && state.LastIndex >= state.CommittedIndex
        && checked(state.LastIndex - state.SnapshotIndex + ReservedTailCommands) < replicaOptions.Value.SnapshotThreshold;
}
