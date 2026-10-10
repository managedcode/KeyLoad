using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.Messaging;

internal static class RemoteTransferPeerEndpointExecution
{
    internal static async Task ExecuteAsync(HttpContext context, RemoteQueueTransferPeerCall call,
        ReadOnlyMemory<byte> body, string signature, RemoteTransferPeerReceiver receiver,
        NodeOptions options, ReplicaMembershipAuthorityAddressPins pins,
        ReplicaMembershipAuthorityReplayCache replay, TimeProvider clock, CancellationToken token)
    {
        RemoteTransferPeerShape.Require(call);
        if (!replay.TryUse(call.Nonce))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid); }
        var address = SiloAddress.FromParsableString(call.CallerSiloAddress);
        var voter = Array.IndexOf(options.MembershipAuthority.AuthorityEndpoints, call.CallerVoter);
        if (voter < RemoteTransferPeerProtocol.SelectedVoter
            || address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid); }
        await pins.PinCallerAsync(voter, address.Endpoint.Address, token).ConfigureAwait(false);
        var remaining = call.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        using var expiry = new CancellationTokenSource(remaining, clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
        var reply = await ExecuteReplyAsync(call, body, signature, receiver, original.Token).ConfigureAwait(false);
        var encoded = RemoteDocumentWire.Encode(reply, call.MaximumReplyBytes);
        using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        context.Response.Headers[RemoteDocumentProtocol.SignatureHeader] = mac.Sign(encoded, reply: true);
        context.Response.ContentType = RemoteDocumentProtocol.ContentType;
        context.Response.ContentLength = encoded.Length;
        await context.Response.Body.WriteAsync(encoded, original.Token).ConfigureAwait(false);
    }

    private static async Task<RemoteDocumentReplyV1> ExecuteReplyAsync(RemoteQueueTransferPeerCall call,
        ReadOnlyMemory<byte> body, string signature, RemoteTransferPeerReceiver receiver, CancellationToken token)
    {
        try
        {
            var result = await receiver.ExecuteAsync(call, body, signature, token).ConfigureAwait(false);
            return new(call.RequestId, call.Nonce, null, null, null, receiver.Discovery(), QueueTransfer: result);
        }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { return new(call.RequestId, call.Nonce, null, error.Code, error.Message, receiver.Discovery()); }
    }
}
