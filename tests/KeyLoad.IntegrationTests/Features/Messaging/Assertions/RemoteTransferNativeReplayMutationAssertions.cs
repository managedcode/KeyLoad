using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeReplayMutationAssertions
{
    // This oracle enumerates the original six cases independently from the production replay mutator.
    internal static void Require(RemoteModelNativeRequest original, RemoteModelNativeRequest actual,
        RemoteModelNativeReplayVariation mode)
    {
        var envelope = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(original.Payload.Span);
        var call = envelope.QueueTransfer ?? throw Missing();
        if (call.Stage != RemoteQueueTransferPeerStage.Accept || !Enum.IsDefined(mode)) { throw Missing(); }
        var expected = mode switch
        {
            RemoteModelNativeReplayVariation.Original2 => envelope,
            RemoteModelNativeReplayVariation.MissingQueueTransfer => envelope with { QueueTransfer = null },
            RemoteModelNativeReplayVariation.ReceiptStage => envelope with { QueueTransfer = call with { Stage = RemoteQueueTransferPeerStage.Receipt } },
            RemoteModelNativeReplayVariation.MissingIntentToken => envelope with { QueueTransfer = call with { IntentToken = string.Empty } },
            RemoteModelNativeReplayVariation.EmptyNonce => envelope with { QueueTransfer = call with { Nonce = string.Empty } },
            RemoteModelNativeReplayVariation.EmptyCommandId => envelope with { QueueTransfer = call with { OriginalCommandId = Guid.Empty } },
            RemoteModelNativeReplayVariation.EmptyDestinationIncarnation => envelope with { QueueTransfer = call with
                { DestinationOwner = call.DestinationOwner with { Owner = call.DestinationOwner.Owner with { Incarnation = Guid.Empty } } } },
            _ => throw Missing()
        };
        var expectedPayload = mode == RemoteModelNativeReplayVariation.Original2
            ? original.Payload : NativeSerialization.Serialize(expected);
        if (!expectedPayload.Span.SequenceEqual(actual.Payload.Span) || original.Signature != actual.Signature
            || original.Nonce == actual.Nonce || actual.Nonce == Guid.Empty)
        { throw Missing(); }
        var expectedOuter = original with { Payload = actual.Payload, Nonce = actual.Nonce, SenderSignature = actual.SenderSignature };
        if (!NativeSerialization.Serialize(expectedOuter).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual)))
        { throw Missing(); }
    }

    private static InvalidOperationException Missing() => new(RemoteTransferDistinctProtocol.Missing);
}
