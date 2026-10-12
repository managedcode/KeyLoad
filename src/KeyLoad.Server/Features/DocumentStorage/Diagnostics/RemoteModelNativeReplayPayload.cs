using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeReplayPayload
{
    internal static ReadOnlyMemory<byte> Create(RemoteModelNativeRequest original, RemoteModelNativeReplayVariation variation)
    {
        if (original.Channel != RemoteModelNativeChannel.DocumentEnvelope || !Enum.IsDefined(variation))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        var envelope = RemoteDocumentWire.DecodeCall(original.Payload.Span);
        var call = envelope.QueueTransfer;
        if (call is null || call.Stage != RemoteQueueTransferPeerStage.Accept)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        if (variation == RemoteModelNativeReplayVariation.Original2) { return original.Payload; }
        if (variation == RemoteModelNativeReplayVariation.MissingQueueTransfer)
        { return RemoteDocumentWire.Encode(envelope with { QueueTransfer = null }); }
        var changed = variation switch
        {
            RemoteModelNativeReplayVariation.ReceiptStage => call with { Stage = RemoteQueueTransferPeerStage.Receipt },
            RemoteModelNativeReplayVariation.MissingIntentToken => call with { IntentToken = string.Empty },
            RemoteModelNativeReplayVariation.EmptyNonce => call with { Nonce = string.Empty },
            RemoteModelNativeReplayVariation.EmptyCommandId => call with { OriginalCommandId = Guid.Empty },
            RemoteModelNativeReplayVariation.EmptyDestinationIncarnation => call with
            { DestinationOwner = call.DestinationOwner with { Owner = call.DestinationOwner.Owner with { Incarnation = Guid.Empty } } },
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof)
        };
        return RemoteDocumentWire.Encode(envelope with { QueueTransfer = changed });
    }
}
