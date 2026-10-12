using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementNativeBlobFaultAssertions
{
    internal static async Task RequireAsync(RemoteModelNativeRequest original, NativeControlledBlobFaultPayload fault,
        RemoteModelNativeRequest actual, RemoteModelNativeReply reply, IReadOnlyList<ReplicaSiloDiscovery> source,
        IReadOnlyList<ReplicaSiloDiscovery> destination, string sourceSecret, string destinationSecret, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.Channel != RemoteModelNativeChannel.DocumentEnvelope || original.Payload.IsEmpty
            || fault.Payload.IsEmpty || fault.Payload.Length > RemoteDocumentProtocol.MaximumBodyBytes
            || !fault.Payload.Span.SequenceEqual(actual.Payload.Span) || fault.Signature != original.Signature
            || actual.Signature != original.Signature || actual.Nonce == Guid.Empty || actual.Nonce == original.Nonce
            || reply.Nonce != actual.Nonce || actual.ExpiresAt != original.ExpiresAt
            || !Exact(original, actual with { Payload = original.Payload, Nonce = original.Nonce, SenderSignature = original.SenderSignature }))
        { throw Missing(); }
        var envelope = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(original.Payload.Span);
        var call = envelope.ControlledBlob ?? throw Missing();
        if (envelope.Document is not null || envelope.Controlled is not null || envelope.QueueTransfer is not null
            || call.CallerVoter != original.CallerVoter || call.CallerSiloAddress != original.CallerSiloAddress
            || !Exact(call.Source, original.SourceOwner) || !Exact(call.Destination.Owner, original.DestinationOwner)
            || call.Request.Frame.ExpiresAt != original.ExpiresAt || reply.Payload.Length > call.MaximumReplyBytes)
        { throw Missing(); }
        PartitionMovementNativeBlobMutationAssertions.Require(call, fault);
        RemoteTransferNativeGenerationAssertions.Require(original, source, destination);
        RemoteTransferNativeGenerationAssertions.Require(actual, source, destination);
        using var sender = RemoteDocumentMac.FromConfiguredSecret(sourceSecret);
        await Assert.That(sender.VerifyNativeRequest(original)).IsTrue();
        await Assert.That(sender.Verify(original.Payload.Span, original.Signature, reply: false)).IsTrue();
        await Assert.That(sender.VerifyNativeRequest(actual)).IsTrue();
        await Assert.That(sender.Verify(actual.Payload.Span, actual.Signature, reply: false)).IsFalse();
        using var receiver = RemoteDocumentMac.FromConfiguredSecret(destinationSecret);
        await Assert.That(receiver.VerifyNativeReply(reply)).IsTrue();
        RequireRefusal(reply, receiver, fault, actual, destination);
        token.ThrowIfCancellationRequested();
    }

    private static void RequireRefusal(RemoteModelNativeReply reply, RemoteDocumentMac receiver,
        NativeControlledBlobFaultPayload fault, RemoteModelNativeRequest actual, IReadOnlyList<ReplicaSiloDiscovery> destination)
    {
        if (reply.Error is not null)
        {
            if (reply.Error != ErrorCode.Unauthenticated || reply.SafeDetail != RemoteModelNativeProtocol.Unavailable
                || !reply.Payload.IsEmpty || reply.Signature is not { Length: 0 })
            { throw Missing(); }
            return;
        }
        if (reply.SafeDetail is not null || reply.Payload.IsEmpty || !receiver.Verify(reply.Payload.Span, reply.Signature, reply: true))
        { throw Missing(); }
        var changed = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(fault.Payload.Span).ControlledBlob
            ?? throw Missing();
        var inner = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(reply.Payload.Span);
        var current = destination.Single(value => value.VoterId == actual.DestinationVoter);
        if (inner.RequestId != changed.RequestId || inner.Nonce != changed.Nonce || inner.Error != ErrorCode.Unauthenticated
            || inner.SafeDetail != RemoteDocumentProtocol.InvalidProof || !Exact(inner.EndpointDiscovery, current)
            || inner.Result is not null || inner.QueryLeaf is not null || inner.Controlled is not null
            || inner.ControlledBlob is not null || inner.SearchLeaf is not null || inner.QueueTransfer is not null)
        { throw Missing(); }
    }

    private static bool Exact<T>(T expected, T actual)
        => NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual));

    private static InvalidOperationException Missing() => new("The actual native blob wire refusal proof is invalid.");
}
