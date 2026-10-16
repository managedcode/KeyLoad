using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.Messaging;
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
        if (original.Channel is not (RemoteModelNativeChannel.ResourceLifetimeBlobOutcome or RemoteModelNativeChannel.ResourceLifetimeRead) || original.Payload.IsEmpty
            || fault.Payload.IsEmpty || fault.Payload.Length > RemoteDocumentProtocol.MaximumBodyBytes
            || !fault.Payload.Span.SequenceEqual(actual.Payload.Span) || fault.Signature != original.Signature
            || actual.Signature != original.Signature || actual.Nonce == Guid.Empty || actual.Nonce == original.Nonce
            || reply.Nonce != actual.Nonce || actual.ExpiresAt != original.ExpiresAt
            || !Exact(original, actual with
            {
                Payload = original.Payload,
                Nonce = original.Nonce,
                SenderSignature = original.SenderSignature
            }))
        {
            throw Missing();
        }
        if (original.Channel == RemoteModelNativeChannel.ResourceLifetimeRead)
        {
            var call = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(original.Payload.Span);
            _ = NativeControlledBlobLifetimeCall.Require(call);
            RemoteResourceLifetimeReadValidation.RequireEnvelope(original, call);
            if (reply.Payload.Length > call.MaximumReplyBytes)
            {
                throw Missing();
            }
            PartitionMovementNativeBlobLifetimeMutationAssertions.Require(call, fault);
        }
        else
        {
            var call = NativeSerialization.Deserialize<RemoteControlledBlobOutcomeCallV2>(original.Payload.Span);
            RemoteControlledBlobOutcomeEndpointV2.RequireEnvelope(original, call);
            if (reply.Payload.Length > call.Original.MaximumReplyBytes)
            {
                throw Missing();
            }
            PartitionMovementNativeBlobMutationAssertions.Require(call, fault);
        }
        RemoteTransferNativeGenerationAssertions.Require(original, source, destination);
        RemoteTransferNativeGenerationAssertions.Require(actual, source, destination);
        using var sender = RemoteDocumentMac.FromConfiguredSecret(sourceSecret);
        await Assert.That(sender.VerifyNativeRequest(original)).IsTrue();
        await Assert.That(sender.Verify(original.Payload.Span, original.Signature, reply: false)).IsTrue();
        await Assert.That(sender.VerifyNativeRequest(actual)).IsTrue();
        await Assert.That(sender.Verify(actual.Payload.Span, actual.Signature, reply: false)).IsFalse();
        using var receiver = RemoteDocumentMac.FromConfiguredSecret(destinationSecret);
        await Assert.That(receiver.VerifyNativeReply(reply)).IsTrue();
        RequireRefusal(reply);
        token.ThrowIfCancellationRequested();
    }

    private static void RequireRefusal(RemoteModelNativeReply reply)
    {
        if (reply.Error != ErrorCode.Unauthenticated || reply.SafeDetail != RemoteModelNativeProtocol.Unavailable
            || !reply.Payload.IsEmpty || reply.Signature is not { Length: 0 })
        {
            throw Missing();
        }
    }

    private static bool Exact<T>(T expected, T actual)
        => NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual));

    private static InvalidOperationException Missing() => new("The actual native blob wire refusal proof is invalid.");
}
