using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3Epoch7MixedRoot
{
    internal static async Task<NodeEpochRf3Inventory[]> CreateAsync(NodeEpochRf3TrialRoots roots,
        string mixedRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(mixedRoot);
        var originals = new NodeEpochRf3Inventory[NodeEpochRf3Protocol.NodeCount];
        originals[0] = await CopyNodeAsync(roots.Current, mixedRoot, NodeEpochRf3Protocol.Node1, cancellationToken)
            .ConfigureAwait(false);
        originals[1] = await CopyNodeAsync(roots.Prior, mixedRoot, NodeEpochRf3Protocol.Node2, cancellationToken)
            .ConfigureAwait(false);
        originals[2] = await CopyNodeAsync(roots.Prior, mixedRoot, NodeEpochRf3Protocol.Node3, cancellationToken)
            .ConfigureAwait(false);
        var profileSha = await roots.Profile!.CopyExactAsync(mixedRoot, roots.ProfileBytes!, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(profileSha).IsEqualTo(roots.ProfileSha256);
        return originals;
    }

    internal static async Task AssertUnchangedAsync(NodeEpochRf3TrialRoots roots, string mixedRoot,
        NodeEpochRf3Inventory[] originals, byte[] profileBytes, CancellationToken cancellationToken)
    {
        for (var index = 0; index < originals.Length; index++)
        {
            var node = NodeName(index);
            var sourceRoot = index == 0 ? roots.Current : roots.Prior;
            var source = await NodeEpochRf3Inventory.CaptureAsync(Path.Combine(sourceRoot, node), cancellationToken)
                .ConfigureAwait(false);
            await Assert.That(originals[index].Equivalent(source)).IsTrue();
        }
        var profile = await NodeEpochRf3Profile.ReadAsync(Path.Combine(mixedRoot,
            NodeEpochRf3Protocol.ProfileFile), cancellationToken).ConfigureAwait(false);
        await Assert.That(profile.Bytes.AsSpan().SequenceEqual(profileBytes)).IsTrue();
    }

    private static async Task<NodeEpochRf3Inventory> CopyNodeAsync(string sourceRoot, string mixedRoot,
        string node, CancellationToken cancellationToken)
    {
        var source = Path.Combine(sourceRoot, node);
        var destination = Path.Combine(mixedRoot, node);
        var inventory = await NodeEpochRf3Inventory.CopyTreeAsync(source, destination, cancellationToken)
            .ConfigureAwait(false);
        var actual = await NodeEpochRf3Inventory.CaptureAsync(destination, cancellationToken).ConfigureAwait(false);
        await Assert.That(inventory.Equivalent(actual)).IsTrue();
        return inventory;
    }

    private static string NodeName(int index) => index switch
    {
        0 => NodeEpochRf3Protocol.Node1,
        1 => NodeEpochRf3Protocol.Node2,
        2 => NodeEpochRf3Protocol.Node3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
