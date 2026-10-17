using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteResourceLifetimeReadExchange
{
    internal static async Task<ReadOnlyMemory<byte>> ReadAsync(RemoteDocumentClient client, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteResourceLifetimeReadCallV2 call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (call.Version != RemoteResourceLifetimeReadProtocol.Version || !Enum.IsDefined(call.Kind)
            || call.RequestId == Guid.Empty || !RemoteModelNativeIdentity.ValidNonce(call.Nonce)
            || call.Payload.IsEmpty || call.ExpiresAt <= clock.GetUtcNow())
        {
            throw RemoteResourceLifetimeReadValidation.Invalid();
        }
        var body = RemoteDocumentWire.Encode(call);
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        var signature = sourceMac.Sign(body, reply: false);
        var original = await client.SendNativeAsync(call.Destination, call.Source, RemoteModelNativeChannel.ResourceLifetimeRead,
            body, signature, call.ExpiresAt, call.MaximumReplyBytes, token).ConfigureAwait(false);
        using var targetMac = RemoteDocumentMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
        if (!targetMac.Verify(original.Payload.Span, original.Signature, reply: true))
        {
            throw RemoteResourceLifetimeReadValidation.Invalid();
        }
        var reply = NativeSerialization.Deserialize<RemoteResourceLifetimeReadReplyV2>(original.Payload.Span);
        RemoteResourceLifetimeReadReplyValidation.Require(call, reply, options, membership, clock, token);
        await client.RequireCurrentReplyAsync(call.Destination, original, reply.EndpointDiscovery.SiloAddress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail!);
        }
        return reply.Payload;
    }
}
