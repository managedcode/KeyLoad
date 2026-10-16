using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Warm
{
    private const long InitialEpoch = 1L;
    private const long ChangedEpoch = 2L;
    private const long DeniedEpoch = 3L;
    internal const long RestoredEpoch = 4L;

    internal static async Task<ResourcePartitionInventoryRf3Baseline> RunAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, CancellationToken token)
    {
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false);
        var baseline = await ResourcePartitionInventoryRf3Assertions.CaptureAsync(wave, seed, membership, InitialEpoch, token);
        await ResourcePartitionInventoryRf3PublicAssertions.ReplayAsync(wave, seed, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3PublicAssertions.LateFailureAsync(wave, seed, token).ConfigureAwait(false);
        await SameAsync(wave, seed, membership, baseline, InitialEpoch, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3Held.RunAsync(wave, seed, membership, cancel: true,
            change: null, token).ConfigureAwait(false);
        await SameAsync(wave, seed, membership, baseline, InitialEpoch, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3Held.RunAsync(wave, seed, membership, cancel: false,
            change: caller => ResourcePartitionInventoryRf3Policy.SetAsync(wave, ChangedEpoch, true, caller), token)
            .ConfigureAwait(false);
        await SameAsync(wave, seed, membership, baseline, ChangedEpoch, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3Policy.SetAsync(wave, DeniedEpoch, false, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3Assertions.RefusalAsync(await ResourcePartitionInventoryRf3Bridge.ReadAsync(
            wave, seed, membership, ResourcePartitionInventoryRf3Protocol.First, token), ErrorCode.PermissionDenied);
        await ResourcePartitionInventoryRf3Policy.SetAsync(wave, RestoredEpoch, true, token).ConfigureAwait(false);
        await SameAsync(wave, seed, membership, baseline, RestoredEpoch, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3UnavailableOwner.RunAsync(wave, seed, membership, token).ConfigureAwait(false);
        using var restarted = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false);
        foreach (var node in TwoRf3MembershipProtocol.Nodes[TwoRf3MembershipProtocol.MembersPerGroup..])
        {
            await Assert.That(restarted.Owners[node].SiloAddress).IsNotEqualTo(membership.Owners[node].SiloAddress);
            await Assert.That(restarted.Owners[node].VoterId).IsEqualTo(membership.Owners[node].VoterId);
            await Assert.That(restarted.Owners[node].Incarnation).IsEqualTo(membership.Owners[node].Incarnation);
        }
        await SameAsync(wave, seed, restarted, baseline, RestoredEpoch, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3PolicyReplay.VerifyAsync(wave, seed, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3PolicyReplay.HealthyAsync(wave, seed, token).ConfigureAwait(false);
        return baseline;
    }

    internal static async Task SameAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        RemoteTransferNativeMembership membership, ResourcePartitionInventoryRf3Baseline baseline, long epoch,
        CancellationToken token)
        => await ResourcePartitionInventoryRf3Assertions.SameMembershipAsync(baseline,
            await ResourcePartitionInventoryRf3Assertions.CaptureAsync(wave, seed, membership, epoch, token));
}
