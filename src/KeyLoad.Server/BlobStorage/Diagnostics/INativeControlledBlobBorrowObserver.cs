using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal interface INativeControlledBlobBorrowObserver
{
    bool Select(RemoteControlledBlobOutcomeCallV2 original, CancellationToken originalCancellationToken);
    ValueTask ObserveAsync(RemoteControlledBlobOutcomeCallV2 original, RemoteModelNativeRequest signedOriginal,
        NativeControlledBlobBorrowedCatalog borrowedCatalog, CancellationToken originalCancellationToken);
    bool Select(RemoteResourceLifetimeReadCallV2 original, CancellationToken originalCancellationToken);
    ValueTask ObserveAsync(RemoteResourceLifetimeReadCallV2 original, RemoteModelNativeRequest signedOriginal,
        NativeControlledBlobBorrowedCatalog borrowedCatalog, CancellationToken originalCancellationToken);
}
