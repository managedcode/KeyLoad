using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Cold
{
    internal static async Task ReopenAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var originalRoot = wave.OwnedDataRoot;
        var stoppedRoot = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        await Assert.That(stoppedRoot).IsEqualTo(originalRoot);
        await Assert.That(wave.applicationDisposed).IsTrue();
        await Assert.That(wave.nodeLocksReleased).IsTrue();
        wave.AssertAllNodeLocksReleased();
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(token).ConfigureAwait(false);
        await Assert.That(wave.OwnedDataRoot).IsEqualTo(originalRoot);
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
    }
}
