using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageRecoveryImages
{
    internal static async Task<NativeDropPageRecoveryImage[]> CaptureAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        var barriers = profile.Hosts.Nodes.Select(node => node.Partition.Coordinator.ReadBarrierAsync(token)).ToArray();
        await KeyLoad.RecoveryHost.Features.ClusterRouting.ControlledPartitionMovementNativeHosts.JoinOriginalAsync(
            Task.WhenAll(barriers)).ConfigureAwait(false);
        return profile.Hosts.Nodes.Select(node => new NativeDropPageRecoveryImage(
            ControlledPartitionMovementRawImage.Bytes(node.Store), node.Store.Position, node.Log.State.LastIndex,
            node.Log.State.CommittedIndex, node.Log.State.Term)).ToArray();
    }

    internal static async Task SameAsync(NativeDropRuntimeProfile profile, NativeDropPageRecoveryImage[] before, CancellationToken token)
    {
        var after = await CaptureAsync(profile, token).ConfigureAwait(false);
        await Assert.That(after.Length).IsEqualTo(before.Length);
        for (var index = default(int); index < before.Length; index++)
        {
            await NativeDropRuntimeAssertions.SameAsync(before[index].Rows, after[index].Rows).ConfigureAwait(false);
            await Assert.That(after[index].Position).IsEqualTo(before[index].Position);
            await Assert.That(after[index].LastIndex).IsEqualTo(before[index].LastIndex);
            await Assert.That(after[index].CommittedIndex).IsEqualTo(before[index].CommittedIndex);
            await Assert.That(after[index].Term).IsEqualTo(before[index].Term);
        }
    }
}
