namespace KeyLoad.Server;

internal static class PartitionHostDisposal
{
    internal static void RequireTransferVerifierClosed(Features.Messaging.RemoteTransferPeerVerificationOwner? verification)
    {
        if (verification is { IsDisposed: false })
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Core.Features.Messaging.RemoteTransferPeerProtocol.Unavailable);
        }
    }

    internal static async Task RunAsync(PartitionHost host, Func<Task, Task> closeLog,
        Func<Task, Task> closeStores, Action<List<Exception>> disposeVisibility)
    {
        host.TransferVerification?.RequireJoined();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => host.OnlineTextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => host.TextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => host.AnnMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => host.Coordinator.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => host.Consensus.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        var applying = host.Materializer.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => applying, failures).ConfigureAwait(false);
        if (host.TransferVerification is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await applying.ConfigureAwait(false);
                host.TransferVerification.Dispose();
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.Observe(host.ApplyProbe.CloseFrameObservation, failures);
        var closingLog = closeLog(applying);
        await ServerFailureObserver.ObserveAsync(() => closingLog, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(host.TextProjection.Dispose, failures);
        disposeVisibility(failures);
        ServerFailureObserver.Observe(host.CacheMemory.Dispose, failures);
        var closingStores = closeStores(closingLog);
        await ServerFailureObserver.ObserveAsync(() => closingStores, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
