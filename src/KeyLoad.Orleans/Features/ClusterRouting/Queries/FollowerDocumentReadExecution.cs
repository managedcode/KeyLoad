using KeyLoad.Core;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal sealed class FollowerDocumentReadExecution(DatabaseEngine database, ICommitCoordinator coordinator,
    ReplicaConsensus consensus, PhysicalShardRecord owner, GrainRequestCodec codec)
{
    private const long UnestablishedConsensusTerm = 0;

    internal async Task<FollowerDocumentReadResultV1> ExecuteAsync(DecodedGrainRequest request,
        IGrainContext context, NativeCapabilityWorkLifetime work)
    {
        var capability = GrainNativePayload.Read<FollowerDocumentReadCapability>(request.Payload);
        DatabaseEngine.ValidateFollowerReadRequest(capability.Request);
        var before = await RequireFollowerAsync(capability.Request.ReplicaId, work.Token).ConfigureAwait(true);
        work.Stage = GrainFailureStage.CapabilityExecution;
        work.BeginPhase(DatabasePhaseKind.AuthorizedReadCapability);
        var snapshot = database.CaptureFollowerDocument(request.Envelope.PrincipalId!, capability.Request,
            owner, before.VoterId, before.Term, work.Token);
        work.CompletePhase();
        var captured = await RequireFollowerAsync(before.VoterId, work.Token).ConfigureAwait(true);
        if (captured.Term != before.Term || captured.LeaderId != before.LeaderId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, DatabaseEngine.FollowerReadOwnerChanged); }
        if (codec.HasPhaseObserver)
        {
            await codec.ObservePhaseAsync(request, GrainRequestPhase.AuthorizationReload, context,
                work.Token).ConfigureAwait(true);
        }
        work.Stage = GrainFailureStage.QuorumRead;
        work.BeginPhase(DatabasePhaseKind.AuthorizedOperationBarrier);
        await coordinator.ReadBarrierAsync(work.Token).ConfigureAwait(true);
        work.CompletePhase();
        work.Token.ThrowIfCancellationRequested();
        codec.ValidateScope(request.Envelope);
        GrainIdentityContext.Validate(request.Envelope, work.RequestId);
        var authority = await RequireFollowerAsync(before.VoterId, work.Token).ConfigureAwait(true);
        work.Stage = GrainFailureStage.CapabilityExecution;
        work.BeginPhase(DatabasePhaseKind.AuthorizedReadCapability);
        var result = database.CompleteFollowerDocument(request.Envelope.PrincipalId!, capability,
            snapshot, authority.VoterId, authority.Term, work.Token);
        work.CompletePhase();
        var completed = await RequireFollowerAsync(before.VoterId, work.Token).ConfigureAwait(true);
        if (completed.Term != authority.Term || completed.LeaderId != authority.LeaderId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, DatabaseEngine.FollowerReadOwnerChanged); }
        work.Token.ThrowIfCancellationRequested();
        codec.ValidateScope(request.Envelope);
        GrainIdentityContext.Validate(request.Envelope, work.RequestId);
        return result;
    }

    private async Task<ReplicaNodeState> RequireFollowerAsync(string expectedReplica, CancellationToken token)
    {
        var state = await consensus.StateAsync(token).ConfigureAwait(true);
        if (state.Role != ReplicaRole.Follower || state.VoterId != expectedReplica
            || !state.TransportReady || state.Term <= UnestablishedConsensusTerm || state.LeaderId is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, DatabaseEngine.FollowerReadReplicaUnavailable); }
        return state;
    }
}
