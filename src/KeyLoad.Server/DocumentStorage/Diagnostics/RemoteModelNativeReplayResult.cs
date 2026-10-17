namespace KeyLoad.Server.Features.DocumentStorage;

// Local actual-RPC observation; this is not a transported or persisted success receipt.
internal sealed record RemoteModelNativeReplayResult(RemoteModelNativeRequest Request, RemoteModelNativeReply Reply);
