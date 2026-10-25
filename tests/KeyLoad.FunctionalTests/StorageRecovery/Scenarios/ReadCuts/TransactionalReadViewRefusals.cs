using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class TransactionalReadViewRefusals
{
    internal static async Task WhileHeldAsync(TransactionalReadViewState state, ZoneTreeReadCutLease lease)
    {
        var image = TransactionalReadViewCommands.QueueImage(state.Store);
        var position = state.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var callbacks = 0;
            var concurrent = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix([], (_, _) => { callbacks++; return true; }));
            await Assert.That(concurrent.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(concurrent.Message).IsEqualTo("A execution read-cut lease permits only one active traversal.");
            await Assert.That(callbacks).IsEqualTo(0);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SecondCaptureAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewValues.ConservedAsync(state, image, position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task SecondCaptureAsync(TransactionalReadViewState state)
    {
        ZoneTreeReadCutLease? unexpected = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => unexpected = state.Capture());
            await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(error.Message).IsEqualTo("The runtime already owns a execution read-cut lease.");
        }, failures);
        if (unexpected is not null)
        {
            ServerFailureObserver.Observe(unexpected.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task DisposedAsync(ZoneTreeReadCutLease lease)
    {
        var callbacks = 0;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix([], (_, _) => { callbacks++; return true; }));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(error.Message).IsEqualTo("The execution read-cut lease is closed.");
        await Assert.That(callbacks).IsEqualTo(0);
    }
    internal static async Task CancellationAsync(TransactionalReadViewState state)
    {
        var image = TransactionalReadViewCommands.QueueImage(state.Store);
        var position = state.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CancelledCaptureAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => CancelTraversalAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewValues.ConservedAsync(state, image, position), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var healthy = state.Capture();
            await TransactionalReadViewValues.CutAsync(state, healthy.Cut, position);
            await Assert.That(TransactionalReadViewValues.Read(healthy))
                .IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CancelledCaptureAsync(TransactionalReadViewState state)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        ZoneTreeReadCutLease? unexpected = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var error = Assert.ThrowsExactly<OperationCanceledException>(() => unexpected = state.Capture(cancelled.Token));
            await Assert.That(error.CancellationToken.IsCancellationRequested).IsTrue();
        }, failures);
        if (unexpected is not null)
        {
            ServerFailureObserver.Observe(unexpected.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CancelTraversalAsync(TransactionalReadViewState state)
    {
        using var during = new CancellationTokenSource();
        using var lease = state.Capture(during.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var callbacks = 0;
            var error = Assert.ThrowsExactly<OperationCanceledException>(() => lease.VisitPrefix([], (_, _) =>
            {
                callbacks++;
                during.Cancel();
                return true;
            }));
            await Assert.That(error.CancellationToken).IsEqualTo(lease.CancellationToken);
            await Assert.That(callbacks).IsEqualTo(1);
        }, failures);
        ServerFailureObserver.Observe(lease.Dispose, failures);
        await ServerFailureObserver.ObserveAsync(() => DisposedAsync(lease), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
