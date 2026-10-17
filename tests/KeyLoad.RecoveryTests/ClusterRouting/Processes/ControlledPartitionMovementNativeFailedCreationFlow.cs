using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeFailedCreationFlow
{
    private const int NoFailures = 0;
    private const int Owners = 6;

    internal static async Task RunAsync(CancellationToken cancellationToken)
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
            await Assert.That(acquired.EntriesBeforeAcquisition).IsEmpty();
            await Assert.That(Directory.GetFileSystemEntries(acquired.Root)).IsEquivalentTo(acquired.EntriesBeforeAcquisition);
            await Assert.That(acquired.Nodes.All(node => !Directory.Exists(node))).IsTrue();
            if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && acquired.ModeBeforeAcquisition is { } mode)
            {
                await Assert.That(File.GetUnixFileMode(acquired.Root)).IsEqualTo(mode);
            }
            var before = await RecoveryFileInventory.CaptureAsync(acquired.Root, cancellationToken);
            await Assert.That(before).IsEmpty();
            await CreatedSiloShutdownOperation.RunAsync(acquired.Root, cancellationToken);
            await RecoveryFileInventory.AssertUnchangedAsync(acquired.Root, before, cancellationToken);
            await Assert.That(Directory.GetFileSystemEntries(acquired.Root)).IsEquivalentTo(acquired.EntriesBeforeAcquisition);
        }, checks);
        if (checks.Count != NoFailures)
        {
            checks.InsertRange(NoFailures, original);
            ServerFailureObserver.ThrowIfAny(checks);
        }
    }

    internal static bool HasOriginalCancellation(Exception original, CancellationToken selected)
        => original is OperationCanceledException canceled && canceled.CancellationToken == selected
            || original is AggregateException aggregate && aggregate.InnerExceptions.Any(child => HasOriginalCancellation(child, selected))
            || original.InnerException is { } inner && HasOriginalCancellation(inner, selected);
}
