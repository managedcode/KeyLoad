using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferWireFaultShapes
{
    internal static IEnumerable<byte[]> Enumerate(RemoteTransferHeldWire held)
    {
        var original = held.Original;
        var call = original.QueueTransfer ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        yield return RemoteDocumentWire.Encode(original with { QueueTransfer = null });
        yield return Encode(original, call with { Stage = RemoteQueueTransferPeerStage.Receipt });
        yield return Encode(original, call with { IntentToken = string.Empty });
        yield return Encode(original, call with { Nonce = string.Empty });
        yield return Encode(original, call with { OriginalCommandId = Guid.Empty });
        yield return Encode(original, call with
        {
            DestinationOwner = call.DestinationOwner with
            { Owner = call.DestinationOwner.Owner with { Incarnation = Guid.Empty } }
        });
    }

    private static byte[] Encode(RemoteDocumentTransportEnvelope original, RemoteQueueTransferPeerCall changed)
        => RemoteDocumentWire.Encode(original with { QueueTransfer = changed });
}
