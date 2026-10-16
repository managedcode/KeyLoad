using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleRf3Cold
{
    internal static async Task<ImmutableArray<DropLifecycleRf3ReceiptProof>> ReopenAsync(
        TwoRf3MembershipWave wave, DropTableLifecycleWaveState state, CancellationToken token)
    {
        Dictionary<string, string> previous;
        using (var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false))
        {
            previous = membership.Owners.ToDictionary(pair => pair.Key, pair => pair.Value.SiloAddress, StringComparer.Ordinal);
        }
        var root = wave.OwnedDataRoot;
        var stoppedRoot = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        await Assert.That(stoppedRoot).IsEqualTo(root);
        await Assert.That(wave.applicationDisposed).IsTrue();
        await Assert.That(wave.nodeLocksReleased).IsTrue();
        wave.AssertAllNodeLocksReleased();
        var proofs = ImmutableArray.CreateBuilder<DropLifecycleRf3ReceiptProof>();
        foreach (var drop in state.Drops)
        {
            // No public result enters the independent native-store/log witness.
            proofs.Add(await DropTableLifecycleNativeWitness.CaptureAsync(root, drop.Table,
                drop.ParentId, token).ConfigureAwait(false));
        }
        wave.AssertAllNodeLocksReleased();
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(token).ConfigureAwait(false);
        await Assert.That(wave.OwnedDataRoot).IsEqualTo(root);
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        using var current = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await Assert.That(current.Owners[node].SiloAddress).IsNotEqualTo(previous[node]);
        }
        return proofs.ToImmutable();
    }
}
