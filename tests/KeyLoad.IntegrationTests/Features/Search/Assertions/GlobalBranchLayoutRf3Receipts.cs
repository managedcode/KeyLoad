using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Receipts
{
    internal static async Task InitialAsync(GlobalBranchLayoutRf3State state)
    {
        var source = state.SourceOwner;
        var target = state.TargetOwner;
        foreach (var receipt in new[] { state.Seed.Original.LocalReceipt, state.Seed.SourceDocumentsReceipt, state.Seed.SourceVectorReceipt })
        {
            await TokenAsync(receipt, source, RemotePartitionQueryRf3Seed.Local);
        }
        await TokenAsync(state.Seed.Original.Destination.Receipt, target, RemoteDocumentRf3Protocol.Partition);
        await TokenAsync(state.Seed.DestinationVectorReceipt, target, RemoteDocumentRf3Protocol.Partition);
    }
    private static async Task TokenAsync(CommitReceipt receipt, PhysicalShardRecord owner, PartitionRef partition)
    {
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new CommitToken(owner.Incarnation, partition.AtomicPartitionId,
            receipt.Token.Position, owner.PlacementEpoch), receipt.Token);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, DistributedSearchRf3Callers callers,
        GlobalBranchLayoutRf3State state, CancellationToken token)
    {
        var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var target = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var sourceMcp = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, token);
        var targetMcp = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey, token);
        await GlobalBranchLayoutRf3Current.ObserveAsync(wave, callers, state,
            () => DistributedSearchRf3Replay.AllAsync(source, target, sourceMcp, targetMcp, state.Seed, token), token);
        if (state.Moved is { } moved)
        {
            await GlobalBranchLayoutRf3Current.ObserveAsync(wave, callers, state, async () =>
                await SqlRf3Protocol.EqualAsync(moved,
                    await McpCallerAssertions.SdkSuccessAsync(await source.MovePartitionAsync(state.Move, token))), token);
        }
    }
}
