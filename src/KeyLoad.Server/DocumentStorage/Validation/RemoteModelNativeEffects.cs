using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.EventStreams;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeEffects
{
    internal static bool MayHaveEffects(RemoteModelNativeChannel channel, ReadOnlyMemory<byte> original)
    {
        if (channel is RemoteModelNativeChannel.AnnProjectionObservation or RemoteModelNativeChannel.ResourceLifetimeRead
            or RemoteModelNativeChannel.ResourceLifetimeAnnRead)
        {
            return false;
        }
        if (channel == RemoteModelNativeChannel.EventVector)
        {
            return NativeSerialization.Deserialize<EventVectorRemoteCall>(original.Span).Action == EventVectorRemoteAction.SourceEffect;
        }
        if (channel != RemoteModelNativeChannel.DocumentEnvelope)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
        }
        var call = RemoteDocumentWire.DecodeCall(original.Span);
        if (call.QueueTransfer is { } queue)
        {
            return queue.Stage == RemoteQueueTransferPeerStage.Accept;
        }
        return false;
    }
}
