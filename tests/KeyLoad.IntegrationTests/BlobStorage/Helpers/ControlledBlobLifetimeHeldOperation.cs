using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class ControlledBlobLifetimeHeldOperation
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        GrainReadKind kind, Func<DateTimeOffset, CancellationTokenSource, Task> afterReply,
        ErrorCode? expected, CancellationToken token)
    {
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var hold = new ControlledBlobLifetimeReadHold(wave.QueryControls, seed.Partition, kind);
        Task<ControlledBlobLifetimeReadObservation>? original = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            original = ControlledBlobLifetimeReadCalls.For(seed, kind)(operation.Token);
            var expiry = await hold.WaitAsync(original, membership.Source, token).ConfigureAwait(false);
            await Assert.That(original.IsCompleted).IsFalse();
            await afterReply(expiry, operation).ConfigureAwait(false);
            hold.Release();
            await ControlledBlobLifetimeReadCalls.RequireFailureAsync(await original.ConfigureAwait(false), expected)
                .ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count != 0)
        {
            await ServerFailureObserver.ObserveAsync(operation.CancelAsync, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.Observe(hold.Release, failures);
        if (original is { } producer)
        {
            await ServerFailureObserver.ObserveAsync(async () => { _ = await producer.ConfigureAwait(false); }, failures)
                .ConfigureAwait(false);
        }
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => hold.JoinAsync(membership.Source, cleanup.Token), failures)
            .ConfigureAwait(false);
        if (failures.Count != 0)
        {
            wave.RetainRoots();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
