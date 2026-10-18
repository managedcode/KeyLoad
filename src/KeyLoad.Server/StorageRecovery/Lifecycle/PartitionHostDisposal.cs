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

    internal static async Task RunAsync(PartitionHost host, Action closeStores, Action<List<Exception>> disposeVisibility)
    {
        host.TransferVerification?.RequireJoined();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => host.OnlineTextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => host.TextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => host.AnnMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (host.TransferVerification is not null)
        {
            ServerFailureObserver.Observe(host.TransferVerification.Dispose, failures);
        }
        ServerFailureObserver.Observe(host.TextProjection.Dispose, failures);
        disposeVisibility(failures);
        ServerFailureObserver.Observe(host.CacheMemory.Dispose, failures);
        ServerFailureObserver.Observe(closeStores, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
