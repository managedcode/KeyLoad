using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionStorageStartupWaitAssertions
{
    private const int SingleFailure = 1;
    private const int NoFailures = 0;

    internal static async Task PendingAsync(OrleansNode node, Task independent, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(independent.IsCompleted).IsFalse(), failures);
        using var selectedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task? selected = null;
        ServerFailureObserver.Observe(() => selected = node.WaitForStartupAsync(selectedCancellation.Token), failures);
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
            && error.Code == ErrorCode.OwnershipLost && error.Message == OrleansNodeProtocol.RoutingUnavailable;
        if (!matches)
        {
            ServerFailureObserver.ThrowIfAny(observed);
        }
        await Assert.That(matches).IsTrue();
    }

    internal static async Task StartAsync(WebApplication application, OrleansNode node, CancellationToken token)
    {
        using var callerWaits = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = node.WaitForStartupAsync(callerWaits.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(original.IsCompleted).IsFalse(), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await application.StartAsync(token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count != NoFailures)
        {
            await ServerFailureObserver.ObserveAsync(callerWaits.CancelAsync, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => original, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(original.IsCompletedSuccessfully).IsTrue();
    }
}
