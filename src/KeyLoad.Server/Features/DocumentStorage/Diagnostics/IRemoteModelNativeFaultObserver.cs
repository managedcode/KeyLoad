namespace KeyLoad.Server.Features.DocumentStorage;

// Optional genuine private-probe observer. It cannot replace dispatch or return a receipt.
internal interface IRemoteModelNativeFaultObserver
{
    ValueTask BeforeDispatchAsync(RemoteModelNativeRequest original, CancellationToken cancellationToken);

    ValueTask AfterDispatchAsync(RemoteModelNativeRequest original, RemoteModelNativeReply actual,
        CancellationToken cancellationToken);
}
