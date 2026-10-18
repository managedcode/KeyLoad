using KeyLoad.RecoveryTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeStartupWaitAssertions
{
    private const int SingleFailure = 1;
    private const int NoFailures = 0;

    internal static async Task PendingAsync(OrleansNode node, Task independent, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(independent.IsCompleted).IsFalse(), failures);
        using var selectedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task? selected = null;
        ServerFailureObserver.Observe(() => selected = node.WaitForNativeStartupAsync(selectedCancellation.Token), failures);
        if (selected is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () => await Assert.That(selected.IsCompleted).IsFalse(), failures);
            await ServerFailureObserver.ObserveAsync(selectedCancellation.CancelAsync, failures);
            await ServerFailureObserver.ObserveAsync(() => SelectedAsync(selected, selectedCancellation.Token), failures);
        }
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(independent.IsCompleted).IsFalse(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SelectedAsync(Task original, CancellationToken token)
    {
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, observed);
        var error = observed.FirstOrDefault() as OperationCanceledException;
        var matches = observed.Count == SingleFailure && original.IsCanceled && error is not null
            && error.CancellationToken == token;
        if (!matches)
        {
            ServerFailureObserver.ThrowIfAny(observed);
        }
        await Assert.That(matches).IsTrue();
    }

    internal static async Task ClosedAsync(Task original)
    {
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, observed);
        var error = observed.FirstOrDefault() as KeyLoadException;
        var matches = observed.Count == SingleFailure && original.IsFaulted && error is not null
            && error.Code == ErrorCode.OwnershipLost && error.Message == PhysicalOwnerProbeProtocol.Unavailable;
        if (!matches)
        {
            ServerFailureObserver.ThrowIfAny(observed);
        }
        await Assert.That(matches).IsTrue();
    }

    internal static async Task StartAsync(ControlledPartitionMovementNativeHosts owners, CancellationToken token)
    {
        using var callerWaits = CancellationTokenSource.CreateLinkedTokenSource(token);
        var originals = new List<Task>();
        var failures = new List<Exception>();
        foreach (var node in owners.Nodes)
        {
            ServerFailureObserver.Observe(() => originals.Add(node.Application.Services.GetRequiredService<OrleansNode>()
                .WaitForNativeStartupAsync(callerWaits.Token)), failures);
        }
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(async () => await Assert.That(original.IsCompleted).IsFalse(), failures);
        }
        var starts = new List<Task>();
        foreach (var node in owners.Nodes)
        {
            ServerFailureObserver.Observe(() => starts.Add(node.StartAsync(token)), failures);
        }
        var starting = Task.WhenAll(starts);
        await ServerFailureObserver.ObserveAsync(() => starting, failures);
        if (failures.Count != NoFailures)
        {
            await ServerFailureObserver.ObserveAsync(callerWaits.CancelAsync, failures);
        }
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(originals.All(original => original.IsCompletedSuccessfully)).IsTrue();
    }
}
