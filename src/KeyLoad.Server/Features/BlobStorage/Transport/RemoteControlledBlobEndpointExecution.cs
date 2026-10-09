using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class RemoteControlledBlobEndpointExecution
{
    internal static async Task ExecuteAsync(HttpContext context, RemoteControlledBlobCall call,
        RemoteControlledBlobReceiver receiver, NodeOptions options, ReplicaMembershipAuthorityAddressPins pins,
        ReplicaMembershipAuthorityReplayCache replay, TimeProvider clock, CancellationToken token)
    {
        receiver.Validate(call, token);
        if (!replay.TryUse(call.Nonce))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var address = SiloAddress.FromParsableString(call.CallerSiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var voter = Array.IndexOf(options.MembershipAuthority.AuthorityEndpoints, call.CallerVoter);
        await pins.PinCallerAsync(voter, address.Endpoint.Address, token).ConfigureAwait(false);
        var remaining = call.Request.Frame.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.Unavailable); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var expiry = new CancellationTokenSource(remaining, clock);
            using var original = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                RemoteDocumentReplyV1 reply;
                try
                {
                    var result = await receiver.ReadAsync(call, original.Token).ConfigureAwait(false);
                    reply = new(call.RequestId, call.Nonce, null, null, null, receiver.Discovery(), ControlledBlob: result);
                }
                catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
                { reply = new(call.RequestId, call.Nonce, null, error.Code, error.Message, receiver.Discovery()); }
                var bytes = RemoteDocumentWire.Encode(reply, call.MaximumReplyBytes);
                using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
                context.Response.Headers[RemoteDocumentProtocol.SignatureHeader] = mac.Sign(bytes, reply: true);
                context.Response.ContentType = RemoteDocumentProtocol.ContentType;
                context.Response.ContentLength = bytes.Length;
                await context.Response.Body.WriteAsync(bytes, original.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
