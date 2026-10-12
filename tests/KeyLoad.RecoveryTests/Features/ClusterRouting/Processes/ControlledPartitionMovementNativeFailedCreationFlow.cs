using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeFailedCreationFlow
{
    private const int NoFailures = 0;
    private const int Owners = 6;
    private const int OwnedLockCount = 18;
    private const string StoreOwnerLock = "owner.lock";

    internal static async Task RunAsync(IOptions<NativeProcessReadinessOptions> readiness,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var selected = new CancellationTokenSource();
        await selected.CancelAsync().ConfigureAwait(false);
        var original = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var unexpected = await ControlledPartitionMovementNativeHosts.CreateAsync(selected.Token);
            unexpected.RetainRoots();
        }, original);
        await Assert.That(original.Count).IsGreaterThan(NoFailures);
        var failure = original.Single();
        var retained = ControlledPartitionMovementNativeFailureRoot.Find(failure);
        var checks = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(HasOriginalCancellation(failure, selected.Token)).IsTrue();
            await Assert.That(retained).IsNotNull();
            var acquired = retained ?? throw new InvalidOperationException(nameof(ControlledPartitionMovementNativeFailureRoot));
            await Assert.That(Directory.Exists(acquired.Root)).IsTrue();
            await Assert.That(acquired.Nodes.Length).IsEqualTo(Owners);
            var before = await RecoveryFileInventory.CaptureAsync(acquired.Root, cancellationToken);
            var opened = NoFailures;
            foreach (var node in acquired.Nodes)
            {
                await ControlledPartitionMovementProcessOwnership.RequireAsync(node, readiness, clock, cancellationToken);
                foreach (var path in new[] { Path.Combine(node, PartitionStoreProtocol.OwnershipFile),
                    Path.Combine(node, PartitionStoreProtocol.CanonicalDirectory, StoreOwnerLock),
                    Path.Combine(node, global::KeyLoad.Replication.ReplicaProtocol.ReplicaDirectory, StoreOwnerLock) })
                {
                    using var ownership = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    opened++;
                }
            }
            await Assert.That(opened).IsEqualTo(OwnedLockCount);
            await RecoveryFileInventory.AssertUnchangedAsync(acquired.Root, before, cancellationToken);
        }, checks);
        if (checks.Count != NoFailures)
        { checks.InsertRange(NoFailures, original); ServerFailureObserver.ThrowIfAny(checks); }
    }

    private static bool HasOriginalCancellation(Exception original, CancellationToken selected)
        => original is OperationCanceledException canceled && canceled.CancellationToken == selected
            || original is AggregateException aggregate && aggregate.InnerExceptions.Any(child => HasOriginalCancellation(child, selected))
            || original.InnerException is { } inner && HasOriginalCancellation(inner, selected);
}
