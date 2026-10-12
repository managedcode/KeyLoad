using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal interface INativeControlledBlobBorrowObserver
{
    bool Select(RemoteControlledBlobCall original, CancellationToken originalCancellationToken);
    ValueTask ObserveAsync(RemoteControlledBlobCall original, RemoteModelNativeRequest signedOriginal,
        NativeControlledBlobBorrowedCatalog borrowedCatalog, CancellationToken originalCancellationToken);
}
