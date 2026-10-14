using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.EventStreams;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeEffects
{
    internal static bool MayHaveEffects(RemoteModelNativeChannel channel, ReadOnlyMemory<byte> original)
    {
        if (channel == RemoteModelNativeChannel.AnnProjectionObservation)
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
        // Controlled reads can durably issue or consume grants. Their uncertain cleanup is an effect boundary.
        return call.Controlled is not null || call.ControlledBlob is not null;
    }
}
