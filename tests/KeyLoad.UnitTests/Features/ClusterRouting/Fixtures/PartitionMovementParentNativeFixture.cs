using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Reuses genuine native owners and MAC admission, recreating admission against each cold database.</summary>
internal sealed class PartitionMovementParentNativeFixture(ControlledPartitionMovementNode source,
    ControlledPartitionMovementLoopbackCorpus corpus, ServerRuntimeOptions runtime, string callerAddress)
{
    internal PartitionMoveParentState Read(PartitionMoveRequest request, Guid selectedId = default,
        string principalId = PhysicalShardCatalogFixture.RootPrincipalId)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var work = new ReadExecutionBudget(runtime.Core.DatabaseLimits, source.Database.EvaluationClock, token);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        return source.Database.ReadPartitionMovementParentState(principalId,
            request, selectedId, work, grant);
    }

    internal async Task<OperationResult> CheckpointAsync(PartitionMoveParentState state, PartitionMoveCheckpointBody body,
        PartitionMovePhaseCommand original)
    {
        var envelope = new PartitionMovePeerEnvelope(PartitionMoveProtocol.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner,
            state.Control is null ? original.ControlIntentDigest : PartitionMoveIntentIdentity.Digest(state.Control),
            PartitionMovePeerStage.ControlCheckpoint, PartitionMoveProtocol.EmptyCount,
            source.Database.EvaluationClock.GetUtcNow() + runtime.GrainRouting.Value.RequestLifetime,
            Guid.NewGuid(), NativeSerialization.Serialize(body));
        return await SubmitAsync(Guid.NewGuid(), envelope);
    }

    internal Task<OperationResult> OriginalAsync(Guid id, PartitionMovePhaseCommand original, DateTimeOffset expiry)
        => SubmitAsync(id, new(original.Version, original.MoveId, original.Partition, original.ControlOwner,
            original.SourcePlacement, original.DestinationOwner, original.ControlIntentDigest, original.Stage,
            original.PageOrdinal, expiry, Guid.NewGuid(), original.Body));

    internal DateTimeOffset OriginalExpiry => source.Database.EvaluationClock.GetUtcNow() + runtime.GrainRouting.Value.RequestLifetime;

    private async Task<OperationResult> SubmitAsync(Guid id, PartitionMovePeerEnvelope envelope)
    {
        OperationResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership,
                runtime.PartitionMovement, source.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
                    new(id, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
                        PartitionMovementTransportAction.Apply, Guid.Empty, PartitionMoveProtocol.EmptyCount),
                    TestContext.Current!.Execution.CancellationToken);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The real parent native fixture has no submitted outcome.");
    }
}
