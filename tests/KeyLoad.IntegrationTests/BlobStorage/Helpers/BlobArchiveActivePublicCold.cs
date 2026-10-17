using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobArchiveActivePublicCold
{
    internal static int Group(ClusterRestoreRf3Fixture target, BlobArchiveActivePublicState state)
        => Array.FindIndex(target.Mappings.ToArray(), mapping => mapping.Source.PhysicalShardId == state.SourceOwner);
    internal static string Node(ClusterRestoreRf3Fixture target, BlobArchiveActivePublicState state)
        => Group(target, state) switch
        {
            0 => TwoRf3MembershipProtocol.Node1,
            1 => TwoRf3MembershipProtocol.Node4,
            _ => throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid)
        };
    internal static async Task RunAsync(ClusterRestoreRf3Fixture target, PartitionMovementPublicParentRf3Seed seed,
        BlobArchiveActivePublicState state, System.Collections.Immutable.ImmutableArray<ClusterBackupOwnerReceipt> originals,
        CancellationToken token)
    {
        await target.StopAsync();
        BlobArchiveActivePublicRaw.Require(target, state, token);
        var cut = ClusterRestoreRf3CanonicalCut.Target(target, originals, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await target.StartAsync(restore: false, token);
            _ = await ClusterRestoreRf3CallerOwner.RunAsync(target.Application, Node(target, state), seed.Credential,
                async (sdk, official) =>
                {
                    await BlobArchiveActivePublicFences.RequireAsync(sdk, official, state, token);
                    await BlobArchiveActivePublicFences.ReplayAsync(sdk, official, state, token);
                    return true;
                }, token);
            await BlobArchiveActivePublicPolicy.ReaderAsync(target, state, token);
            await target.StopAsync();
            BlobArchiveActivePublicRaw.Require(target, state, token);
            await ClusterRestoreRf3CanonicalCut.RequireColdAsync(target, originals, cut, token);
        }
    }
}
