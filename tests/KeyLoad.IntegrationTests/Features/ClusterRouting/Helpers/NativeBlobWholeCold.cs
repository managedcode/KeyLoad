using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholeCold
{
    internal static async Task<IReadOnlyDictionary<string, NativeBlobWholeOwnerCut>> CaptureAsync(
        TwoRf3MembershipWave wave, IReadOnlyDictionary<string, ReplicaSiloDiscovery> signedOwners, CancellationToken token)
    {
        var actualNodes = await RemoteTransferNativeColdIdentities.ReadAsync(wave, signedOwners, token).ConfigureAwait(false);
        try
        {
            var root = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
            if (!wave.applicationDisposed || !wave.nodeLocksReleased || signedOwners.Count != 6)
            { throw new InvalidOperationException("The six original native Blob owners have not joined."); }
            wave.AssertAllNodeLocksReleased();
            var cuts = new Dictionary<string, NativeBlobWholeOwnerCut>(StringComparer.Ordinal);
            foreach (var node in TwoRf3MembershipProtocol.Nodes)
            {
                token.ThrowIfCancellationRequested();
                cuts.Add(node, NativeBlobWholeStoppedOwner.Read(wave, root, node, signedOwners[node], actualNodes[node], token));
            }
            wave.AssertAllNodeLocksReleased();
            return cuts;
        }
        catch (Exception) { wave.RetainRoots(); throw; }
    }

    internal static async Task RestartAsync(TwoRf3MembershipWave wave,
        RemoteTransferNativeAdministrator administrator, IReadOnlyDictionary<string, ReplicaSiloDiscovery> prior, CancellationToken token)
    {
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await administrator.ReopenAsync(wave, token).ConfigureAwait(false);
        using var current = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await Assert.That(current.Owners[node].SiloAddress).IsNotEqualTo(prior[node].SiloAddress);
            await Assert.That(current.Owners[node].VoterId).IsEqualTo(prior[node].VoterId);
            await Assert.That(current.Owners[node].Incarnation).IsEqualTo(prior[node].Incarnation);
            await Assert.That(current.Owners[node].ClusterId).IsEqualTo(prior[node].ClusterId);
        }
    }
}
