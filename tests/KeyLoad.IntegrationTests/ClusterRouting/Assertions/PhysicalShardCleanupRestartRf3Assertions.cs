using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCleanupRestartRf3Assertions
{
    private const int FirstEntry = 0;
    private const int SecondEntry = 1;
    private const int FirstCommittedBatch = 1;
    private const int FirstCompletedFamily = 1;
    private const int InitialGeneration = 0;
    private const long NoAppliedPosition = 0;

    internal static async Task ProofAsync(RequestCqrsMovementJournalRecord proof,
        PartitionMovementPublicParentRf3Seed seed, Guid phase)
    {
        await PhysicalShardPartitionMovementRf3JournalAssertions.ProofAsync(proof, seed, phase, PartitionMovePeerStage.Retire);
        await Assert.That(proof.Journal!.CommandId).IsEqualTo(phase);
        await Assert.That(proof.Journal.EffectDigest).IsNull();
    }

    internal static async Task ColdAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, RequestCqrsMovementJournalRecord proof, string observedNode)
    {
        var original = PhysicalShardPartitionMovementRf3JournalAssertions.ReadPhase(wave, observedNode, proof);
        await Assert.That(original.Error).IsNull();
        await Assert.That(original.SafeDetail).IsNull();
        var cleanup = new PartitionMoveCleanupState(PartitionMoveProtocol.Version, proof.MoveId, proof.Partition,
            proof.Journal!.ControlIntentDigest, PartitionMovePeerStage.Retire, PartitionMoveCleanupRole.Source,
            FirstCompletedFamily, null, FirstCommittedBatch);
        await SqlRf3Protocol.EqualAsync(new PartitionMovePhaseResult(proof.MoveId, PartitionMovePeerStage.Retire,
            proof.Journal, null, null, null, null, null, cleanup), original.Get<PartitionMovePhaseResult>());
        await SqlRf3Protocol.EqualAsync(cleanup, ReadCleanup(wave, observedNode, seeds[FirstEntry]));
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Take(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            // RF3 acknowledgement does not claim every follower has applied before the immediate process cut.
            if (ReadCleanup(wave, node, seeds[FirstEntry]) is { } applied)
            {
                await SqlRf3Protocol.EqualAsync(cleanup, applied);
            }
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var untouched = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node,
                seeds[SecondEntry].FirstRequest, Guid.Empty);
            await Assert.That(untouched.Header).IsNull();
            await Assert.That(untouched.Pending).IsNull();
            await Assert.That(untouched.LastIssued).IsNull();
            var current = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node,
                seeds[FirstEntry].FirstRequest, proof.CommandId);
            if (current.Header is { } header)
            {
                await Assert.That(header.CleanupGeneration).IsEqualTo((long)InitialGeneration);
                await Assert.That(header.TerminalResult).IsNull();
            }
        }
    }

    private static PartitionMoveCleanupState? ReadCleanup(TwoRf3MembershipWave wave, string node,
        PartitionMovementPublicParentRf3Seed seed)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        PartitionMoveCleanupState? state = null;
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            state = store.Read(view => PartitionMoveCleanupStorage.Read(view, seed.Partition,
                seed.FirstRequest.MoveId, new DatabaseLimits().MaxBatchBytes));
            if (store.Position <= NoAppliedPosition)
            {
                throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage);
            }
        }, failures);
        if (store is { } owned)
        {
            ServerFailureObserver.Observe(owned.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return state;
    }
}
