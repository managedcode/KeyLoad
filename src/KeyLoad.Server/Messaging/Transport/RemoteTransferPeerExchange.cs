using KeyLoad.Clustering;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Messaging;

internal static class RemoteTransferPeerExchange
{
    internal static async Task<RemoteQueueTransferPeerResult> SendAsync(RemoteDocumentClient client,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteQueueTransferPeerCall call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RemoteTransferPeerShape.Require(call);
        var body = RemoteDocumentWire.Encode(new RemoteDocumentTransportEnvelope(null, null, QueueTransfer: call));
        try
        {
            using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
            var signature = mac.Sign(body, reply: false);
            var reply = await client.SendNativeAsync(call.DestinationOwner, call.SourceOwner.Owner,
                RemoteModelNativeChannel.DocumentEnvelope, body, signature, call.ExpiresAt,
                call.MaximumReplyBytes, token).ConfigureAwait(false);
            return await ReadAsync(reply, client, options, membership,
                clock, call, token).ConfigureAwait(false);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(body); }
    }

    private static async Task<RemoteQueueTransferPeerResult> ReadAsync(RemoteModelAuthenticatedReply nativeReply,
        RemoteDocumentClient client, NodeOptions options, IOptions<OrleansMembershipOptions> membership,
        TimeProvider clock, RemoteQueueTransferPeerCall call, CancellationToken token)
    {
        byte[]? encoded = null;
        var accepted = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var payload = nativeReply.Payload;
            var signature = nativeReply.Signature;
            if (payload.IsEmpty || payload.Length > call.MaximumReplyBytes
                || signature is not { Length: RemoteDocumentProtocol.SignatureCharacters })
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid);
            }
            encoded = payload.ToArray();
            using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
            if (!mac.Verify(encoded, signature, reply: true))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid);
            }
            var reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(encoded);
            RemoteTransferPeerReplyValidation.Require(call, reply, options, membership, clock, token);
            await client.RequireCurrentReplyAsync(call.DestinationOwner, nativeReply,
                reply.EndpointDiscovery.SiloAddress, token).ConfigureAwait(false);
            accepted = true;
            if (reply.Error is { } error)
            {
                throw Errors.Fail(error, reply.SafeDetail ?? RemoteTransferPeerProtocol.Unavailable);
            }
            return reply.QueueTransfer ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        }
        catch (Exception error) when (!accepted && nativeReply.MayHaveEffects
            && global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteTransferPeerProtocol.Unavailable, error);
        }
        finally
        {
            if (encoded is not null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded);
            }
        }
    }
}
