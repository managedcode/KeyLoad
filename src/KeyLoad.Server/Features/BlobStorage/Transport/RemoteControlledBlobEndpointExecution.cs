using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class RemoteControlledBlobEndpointExecution
{
    internal static Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(RemoteControlledBlobCall call,
        RemoteControlledBlobReceiver receiver, NodeOptions options,
        PartitionMovementReplayCache replay, TimeProvider clock, CancellationToken token)
        => ExecuteAsync(call, receiver, options, replay, clock, null, token);

    internal static async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(RemoteControlledBlobCall call,
        RemoteControlledBlobReceiver receiver, NodeOptions options,
        PartitionMovementReplayCache replay, TimeProvider clock,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeEffectBoundary? boundary, CancellationToken token)
    {
        receiver.Validate(call, token);
        if (!replay.TryUse(call.Nonce))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var remaining = call.Request.Frame.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.Unavailable); }
        using var expiry = new CancellationTokenSource(remaining, clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
        RemoteDocumentReplyV1 reply;
        try
        {
            var result = await receiver.ReadAsync(call, boundary, original.Token).ConfigureAwait(false);
            reply = new(call.RequestId, call.Nonce, null, null, null, receiver.Discovery(), ControlledBlob: result);
        }
        catch (global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeTerminalException terminal)
            when (terminal.Original is { } observedOriginal)
        { reply = new(call.RequestId, call.Nonce, null, observedOriginal.Code, observedOriginal.Message, receiver.Discovery()); }
        catch (KeyLoadException error) when (boundary?.IsStarted == true)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable, error); }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { reply = new(call.RequestId, call.Nonce, null, error.Code, error.Message, receiver.Discovery()); }
        var bytes = RemoteDocumentWire.Encode(reply, call.MaximumReplyBytes);
        using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        original.Token.ThrowIfCancellationRequested();
        return (bytes, mac.Sign(bytes, reply: true));
    }
}
