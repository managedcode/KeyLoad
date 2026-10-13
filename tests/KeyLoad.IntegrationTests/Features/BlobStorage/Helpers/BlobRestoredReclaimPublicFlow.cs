using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRestoredReclaimPublicFlow
{
    internal static int Group(ClusterRestoreRf3Fixture target, BlobRestoredReclaimPublicState state)
        => Array.FindIndex(target.Mappings.ToArray(), mapping => mapping.Source.PhysicalShardId == state.Owner) switch
        { 0 => 0, 1 => 1, _ => throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid) };
    private static string Node(ClusterRestoreRf3Fixture target, BlobRestoredReclaimPublicState state)
        => Group(target, state) == 0 ? TwoRf3MembershipProtocol.Node1 : TwoRf3MembershipProtocol.Node4;
    internal static async Task RunAsync(ClusterRestoreRf3Fixture target, PartitionMovementPublicParentRf3Seed seed,
        BlobRestoredReclaimPublicState state, ImmutableArray<ClusterBackupOwnerReceipt> originals, CancellationToken token)
    {
        var incarnation = target.Mappings[Group(target, state)].Target.Incarnation;
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(target.Application, Node(target, state), seed.Credential,
            async (sdk, official) => { await BlobRestoredReclaimPublicOperations.PartialAsync(sdk, official, state, incarnation, token); return true; }, token);
        await target.StopAsync();
        BlobRestoredReclaimPublicRaw.Require(target, state, false, token);
        var partialCut = ClusterRestoreRf3CanonicalCut.Target(target, originals, token);
        await target.StartAsync(restore: false, token);
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(target.Application, Node(target, state), seed.Credential,
            async (sdk, official) => { await BlobRestoredReclaimPublicOperations.ReplayAsync(sdk, official, state, false, token); return true; }, token);
        await target.StopAsync();
        BlobRestoredReclaimPublicRaw.Require(target, state, false, token);
        await ClusterRestoreRf3CanonicalCut.RequireColdAsync(target, originals, partialCut, token);
        await target.StartAsync(restore: false, token);
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(target.Application, Node(target, state), seed.Credential,
            async (sdk, official) => { await BlobRestoredReclaimPublicOperations.FinalAsync(sdk, official, state, incarnation, token); return true; }, token);
        await target.StopAsync();
        BlobRestoredReclaimPublicRaw.Require(target, state, true, token);
        var finalCut = ClusterRestoreRf3CanonicalCut.Target(target, originals, token);
        await target.StartAsync(restore: false, token);
        _ = await ClusterRestoreRf3CallerOwner.RunAsync(target.Application, Node(target, state), seed.Credential,
            async (sdk, official) => { await BlobRestoredReclaimPublicOperations.ReplayAsync(sdk, official, state, true, token); return true; }, token);
        await target.StopAsync();
        BlobRestoredReclaimPublicRaw.Require(target, state, true, token);
        await ClusterRestoreRf3CanonicalCut.RequireColdAsync(target, originals, finalCut, token);
    }
}
