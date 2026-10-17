using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3UnavailableOwner
{
    private const string Scenario = "resource-owner-inventory-required-remote-group-unavailable";

    internal static async Task RunAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        RemoteTransferNativeMembership membership, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await KillAsync(wave, token).ConfigureAwait(false);
            var reply = await ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, seed, membership,
                ResourcePartitionInventoryRf3Protocol.First, token).ConfigureAwait(false);
            await Assert.That(token.IsCancellationRequested).IsFalse();
            await Assert.That(reply.Witness).IsNull();
            await Assert.That(reply.Error is ErrorCode.RecoveryRequired or ErrorCode.CursorExpired).IsTrue();
        }, failures).ConfigureAwait(false);
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => RestoreAsync(wave, cleanup.Token), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task KillAsync(TwoRf3MembershipWave wave, CancellationToken token)
        => JoinAsync(TwoRf3MembershipProtocol.Nodes[TwoRf3MembershipProtocol.MembersPerGroup..]
            .Select(node => wave.RemoteRuntime.KillAsync(node, Scenario, token)).ToArray());

    private static async Task RestoreAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        await JoinAsync(TwoRf3MembershipProtocol.Nodes[TwoRf3MembershipProtocol.MembersPerGroup..]
            .Select(node => wave.RemoteRuntime.RestartAsync(node, token)).ToArray()).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(token).ConfigureAwait(false);
    }

    private static async Task JoinAsync(Task[] originals)
    {
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
