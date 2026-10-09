namespace KeyLoad.Server.Features.BlobStorage;

/// <summary>Borrows actual signed bytes only within the original admitted native request lifetime.</summary>
internal interface IControlledBlobWireBorrowObserver
{
    ValueTask ObserveAsync(RemoteControlledBlobCall call, ReadOnlyMemory<byte> envelope,
        string signature, CancellationToken cancellationToken);
}
