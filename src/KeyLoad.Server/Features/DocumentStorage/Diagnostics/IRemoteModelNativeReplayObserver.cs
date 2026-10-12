namespace KeyLoad.Server.Features.DocumentStorage;

internal interface IRemoteModelNativeReplayObserver
{
    RemoteModelNativeReplayVariation? Select(RemoteModelNativeRequest original, CancellationToken cancellationToken);
    ValueTask ObserveAsync(RemoteModelNativeRequest original, RemoteModelNativeReplayExchange owner, CancellationToken cancellationToken);
}
