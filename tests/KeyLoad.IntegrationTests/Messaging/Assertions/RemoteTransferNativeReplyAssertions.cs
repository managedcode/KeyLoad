using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeReplyAssertions
{
    internal static async Task RequireAsync(RemoteModelNativeRequest original, RemoteModelNativeReply actual,
        string configuredSourceSecret, string configuredDestinationSecret, RemoteTransferPostAwaitNativeCut committed)
    {
        if (original.Channel != RemoteModelNativeChannel.DocumentEnvelope
            || original.Nonce == Guid.Empty || string.IsNullOrEmpty(original.SenderSignature)
            || actual.Nonce != original.Nonce || actual.Error is not null || actual.SafeDetail is not null)
        {
            throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        }
        var envelope = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(original.Payload.Span);
        var call = envelope.QueueTransfer
            ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        if (call.Stage != RemoteQueueTransferPeerStage.Accept
            || call.OriginalCommandId == Guid.Empty || call.MaximumReplyBytes <= 0
            || call.MaximumReplyBytes > RemoteDocumentProtocol.MaximumBodyBytes
            || actual.Payload.IsEmpty || actual.Payload.Length > call.MaximumReplyBytes)
        {
            throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        }
        using var source = RemoteDocumentMac.FromConfiguredSecret(configuredSourceSecret);
        await Assert.That(source.VerifyNativeRequest(original)).IsTrue();
        await Assert.That(source.Verify(original.Payload.Span, original.Signature, reply: false)).IsTrue();
        using var mac = RemoteDocumentMac.FromConfiguredSecret(configuredDestinationSecret);
        await Assert.That(mac.VerifyNativeReply(actual)).IsTrue();
        await Assert.That(mac.Verify(actual.Payload.Span, actual.Signature, reply: true)).IsTrue();
        var reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(actual.Payload.Span);
        if (reply.RequestId != call.RequestId || reply.Nonce != call.Nonce || reply.Error is not null
            || reply.SafeDetail is not null || reply.Result is not null || reply.QueryLeaf is not null
            || reply.Controlled is not null || reply.ControlledBlob is not null || reply.SearchLeaf is not null
            || reply.QueueTransfer is not
            {
                Stage: RemoteQueueTransferPeerStage.Accept, Receipt: null,
                OriginalOutcome: { } outcome
            })
        {
            throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        }
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(outcome))).IsEqualTo(committed.Outcome);
    }
}
