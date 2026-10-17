using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.Messaging;

internal static class RemoteTransferPeerEndpointExecution
{
    internal static Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        RemoteQueueTransferPeerCall call, ReadOnlyMemory<byte> body, string signature,
        RemoteTransferPeerReceiver receiver, NodeOptions options, global::KeyLoad.Server.Features.ClusterRouting.PartitionMovementReplayCache replay,
        TimeProvider clock, CancellationToken token)
        => ExecuteAsync(call, body, signature, receiver, options, replay, clock, null, token);

    internal static async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        RemoteQueueTransferPeerCall call, ReadOnlyMemory<byte> body, string signature,
        RemoteTransferPeerReceiver receiver, NodeOptions options, global::KeyLoad.Server.Features.ClusterRouting.PartitionMovementReplayCache replay,
        TimeProvider clock,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeEffectBoundary? effectBoundary, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (call is null || !RemoteTransferPeerNonce.Valid(call.Nonce))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid);
        }
        RemoteTransferPeerShape.Require(call);
        if (!replay.TryUse(call.Nonce))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid);
        }
        var remaining = call.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid);
        }
        using var expiry = new CancellationTokenSource(remaining, clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
        var reply = await ExecuteReplyAsync(call, body, signature, receiver, effectBoundary, original.Token).ConfigureAwait(false);
        var encoded = RemoteDocumentWire.Encode(reply, call.MaximumReplyBytes);
        var transferred = false;
        try
        {
            original.Token.ThrowIfCancellationRequested();
            using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
            var replySignature = mac.Sign(encoded, reply: true);
            original.Token.ThrowIfCancellationRequested();
            transferred = true;
            return (encoded, replySignature);
        }
        finally
        {
            if (!transferred)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded);
            }
        }
    }

    private static async Task<RemoteDocumentReplyV1> ExecuteReplyAsync(RemoteQueueTransferPeerCall call,
        ReadOnlyMemory<byte> body, string signature, RemoteTransferPeerReceiver receiver,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeEffectBoundary? effectBoundary, CancellationToken token)
    {
        try
        {
            var result = await receiver.ExecuteAsync(call, body, signature, effectBoundary, token).ConfigureAwait(false);
            return new(call.RequestId, call.Nonce, null, null, null, receiver.Discovery(), QueueTransfer: result);
        }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        {
            if (effectBoundary?.IsStarted == true)
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteTransferPeerProtocol.Unavailable, error);
            }
            return new(call.RequestId, call.Nonce, null, error.Code, error.Message, receiver.Discovery());
        }
    }
}
