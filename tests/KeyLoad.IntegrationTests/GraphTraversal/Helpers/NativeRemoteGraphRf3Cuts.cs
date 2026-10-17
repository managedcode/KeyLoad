using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphRf3Cuts
{
    internal static async Task<IReadOnlyDictionary<string, NativeBlobWholeOwnerCut>> CaptureAsync(
        TwoRf3MembershipWave wave, PhysicalOwnerDirectoryV1 directory, CancellationToken token)
    {
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        var identities = await RemoteTransferNativeColdIdentities.ReadAsync(wave, membership.Owners, token);
        var root = await wave.StopForDirectoryReadAsync();
        wave.AssertAllNodeLocksReleased();
        var cuts = new Dictionary<string, NativeBlobWholeOwnerCut>(StringComparer.Ordinal);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            token.ThrowIfCancellationRequested();
            cuts.Add(node, NativeRemoteGraphRf3StoppedOwner.Read(wave, root, node,
                membership.Owners[node], identities[node], directory, token));
        }
        return cuts;
    }
}
