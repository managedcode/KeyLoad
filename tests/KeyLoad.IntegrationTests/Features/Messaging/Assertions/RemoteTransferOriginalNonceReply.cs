using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferOriginalNonceReply
{
    internal static async Task RequireAsync(HttpResponseMessage response, RemoteTransferHeldWire held,
        PartitionMovementLateNativeOwners owners, RemoteTransferPostAwaitNativeCut native, CancellationToken token)
    {
        var call = held.Original.QueueTransfer ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        var encoded = await RemoteDocumentWire.ReadReplyAsync(response.Content, token, call.MaximumReplyBytes);
        if (!response.Headers.TryGetValues(RemoteDocumentProtocol.SignatureHeader, out var signatures))
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        var signature = signatures.Single();
        var options = owners.Nodes[held.Receiver].Application.Services.GetRequiredService<IOptions<NodeOptions>>().Value;
        using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        await Assert.That(mac.Verify(encoded, signature, reply: true)).IsTrue();
        var reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(encoded);
        if (reply.RequestId != call.RequestId || reply.Nonce != call.Nonce || reply.Error is not null
            || reply.SafeDetail is not null || reply.Result is not null || reply.QueryLeaf is not null
            || reply.Controlled is not null || reply.ControlledBlob is not null || reply.SearchLeaf is not null
            || reply.QueueTransfer is not
            {
                Stage: RemoteQueueTransferPeerStage.Accept, Receipt: null,
                OriginalOutcome: { } outcome
            })
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(outcome))).IsEqualTo(native.Outcome);
    }
}
