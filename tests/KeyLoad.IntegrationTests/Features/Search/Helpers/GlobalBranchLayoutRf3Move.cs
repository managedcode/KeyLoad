using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Move
{
    internal static async Task<GlobalBranchLayoutRf3State> RunAsync(TwoRf3MembershipWave wave,
        DistributedSearchRf3Callers callers, GlobalBranchLayoutRf3State state, CancellationToken token)
    {
        var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await source.MovePartitionAsync(state.Move, token));
        var installed = actual.InstalledReceipt ?? throw new InvalidOperationException("The original layout move has no installed receipt.");
        var epoch = checked(Math.Max(state.Original.PlacementEpoch, state.TargetOwner.PlacementEpoch) + 1L);
        var publication = checked(state.Original.DirectoryRevision + 1L);
        var placement = new AtomicPartitionPlacementV1(GlobalBranchLayoutRf3State.Version, state.Move.Partition,
            state.TargetOwner.PhysicalShardId, publication, state.TargetOwner.Incarnation, state.TargetOwner.VoterIds, epoch);
        await Assert.That(actual.SourceCut).IsGreaterThan(0L);
        await Assert.That(installed.Position).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new CommitToken(state.TargetOwner.Incarnation, state.Move.Partition.AtomicPartitionId,
            installed.Position, state.TargetOwner.PlacementEpoch), installed);
        await SqlRf3Protocol.EqualAsync(new PartitionMoveResult(state.Move.MoveId, state.Move.Partition,
            PartitionMovePhase.Retired, state.SourceOwner, state.TargetOwner, actual.SourceCut, installed, placement), actual);
        var current = await McpCallerAssertions.SdkSuccessAsync(await source.ReadAtomicPartitionPlacementAsync(
            new(GlobalBranchLayoutRf3State.Version, state.Move.Partition), token));
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(GlobalBranchLayoutRf3State.Version,
            state.Move.Partition, state.TargetOwner.PhysicalShardId, state.TargetOwner.Incarnation, state.TargetOwner.VoterIds,
            epoch, publication, publication, false), current);
        return state with
        {
            Moved = actual
        };
    }
}
