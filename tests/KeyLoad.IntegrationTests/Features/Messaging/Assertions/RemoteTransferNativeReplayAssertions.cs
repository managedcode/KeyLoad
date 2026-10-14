using KeyLoad.Clustering;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeReplayAssertions
{
    internal static async Task<(RemoteModelNativeRequest Original, RemoteModelNativeRequest Request, RemoteModelNativeReply Reply)>
        RequireAsync(RequestCqrsNativeReplayWitness witness, RemoteModelNativeReplayVariation mode, int ordinal,
            CommandRequest accept, IReadOnlyList<ReplicaSiloDiscovery> source, IReadOnlyList<ReplicaSiloDiscovery> destination,
            string sourceSecret, string destinationSecret, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Use the frozen owner codec for strict required fields, original bytes and complete8192 bound.
        _ = RequestCqrsNativeReplayInventory.ReadWitness(RequestCqrsNativeReplayInventory.WriteWitness(witness));
        if (witness.ReplayMode != mode || witness.Ordinal != ordinal || witness.CommandId != accept.CommandId)
        {
            throw Missing();
        }
        var original = RequestCqrsNativeReplayInventory.Original(witness);
        var request = RequestCqrsNativeReplayInventory.Request(witness);
        var reply = RequestCqrsNativeReplayInventory.Reply(witness);
        RequireAccept(original, witness, accept);
        RemoteTransferNativeGenerationAssertions.Require(original, source, destination);
        RemoteTransferNativeGenerationAssertions.Require(request, source, destination);
        RemoteTransferNativeReplayMutationAssertions.Require(original, request, mode);
        using var sender = RemoteDocumentMac.FromConfiguredSecret(sourceSecret);
        await Assert.That(sender.VerifyNativeRequest(original)).IsTrue();
        await Assert.That(sender.Verify(original.Payload.Span, original.Signature, reply: false)).IsTrue();
        await Assert.That(sender.VerifyNativeRequest(request)).IsTrue();
        await Assert.That(sender.Verify(request.Payload.Span, request.Signature, reply: false))
            .IsEqualTo(mode == RemoteModelNativeReplayVariation.Original2);
        using var receiver = RemoteDocumentMac.FromConfiguredSecret(destinationSecret);
        await Assert.That(receiver.VerifyNativeReply(reply)).IsTrue();
        if (reply.Nonce != request.Nonce)
        {
            throw Missing();
        }
        if (mode != RemoteModelNativeReplayVariation.Original2 || ordinal == 2)
        {
            RequireRefusal(request, reply, receiver);
        }
        else if (reply.Error is not null || reply.SafeDetail is not null || reply.Payload.IsEmpty
            || !receiver.Verify(reply.Payload.Span, reply.Signature, reply: true))
        {
            throw Missing();
        }
        if (reply.Error is null)
        {
            var inner = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(reply.Payload.Span);
            var signedTarget = destination.Single(value => value.VoterId == request.DestinationVoter);
            if (!Exact(inner.EndpointDiscovery, signedTarget))
            {
                throw Missing();
            }
        }
        token.ThrowIfCancellationRequested();
        return (original, request, reply);
    }

    private static void RequireAccept(RemoteModelNativeRequest original, RequestCqrsNativeReplayWitness witness, CommandRequest accept)
    {
        var envelope = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(original.Payload.Span);
        var call = envelope.QueueTransfer ?? throw Missing();
        if (accept.Mutations.Length != 1 || accept.Mutations[0] is not AcceptQueueTransfer command
            || call.Stage != RemoteQueueTransferPeerStage.Accept || call.RequestId != witness.RequestId
            || call.OriginalCommandId != accept.CommandId || call.IntentToken != command.IntentToken
            || call.IntentClaims.Destination != command.DestinationQueue || command.DestinationQueue.Partition != accept.Partition
            || call.CallerVoter != original.CallerVoter || call.CallerSiloAddress != original.CallerSiloAddress
            || call.ExpiresAt != original.ExpiresAt || call.ExpiresAt.Offset != TimeSpan.Zero
            || !Guid.TryParseExact(call.Nonce, "N", out var nonce) || nonce == Guid.Empty || nonce.ToString("N") != call.Nonce)
        {
            throw Missing();
        }
        if (!Exact(call.SourceOwner.Owner, original.SourceOwner) || !Exact(call.DestinationOwner.Owner, original.DestinationOwner))
        {
            throw Missing();
        }
    }

    private static void RequireRefusal(RemoteModelNativeRequest request, RemoteModelNativeReply reply, RemoteDocumentMac mac)
    {
        if (reply.Error is not null)
        {
            if (reply.Error != ErrorCode.Unauthenticated || !reply.Payload.IsEmpty || reply.Signature is not { Length: 0 }
                || reply.SafeDetail != RemoteModelNativeProtocol.Unavailable)
            {
                throw Missing();
            }
            return;
        }
        if (reply.SafeDetail is not null || reply.Payload.IsEmpty || !mac.Verify(reply.Payload.Span, reply.Signature, reply: true))
        {
            throw Missing();
        }
        var actual = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(reply.Payload.Span);
        var call = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(request.Payload.Span).QueueTransfer;
        if (call is null || actual.RequestId != call.RequestId || actual.Nonce != call.Nonce
            || actual.Error != ErrorCode.Unauthenticated || actual.Result is not null || actual.QueryLeaf is not null
            || actual.Controlled is not null || actual.ControlledBlob is not null || actual.SearchLeaf is not null
            || actual.QueueTransfer is not null || actual.SafeDetail is not (RemoteTransferPeerProtocol.Invalid or RemoteDocumentProtocol.InvalidProof)
            || actual.EndpointDiscovery.SiloAddress != request.DestinationSiloAddress
            || actual.EndpointDiscovery.VoterId != request.DestinationVoter
            || actual.EndpointDiscovery.Incarnation != request.DestinationOwner.Incarnation)
        {
            throw Missing();
        }
    }

    private static bool Exact<T>(T left, T right) => NativeSerialization.Serialize(left).AsSpan().SequenceEqual(NativeSerialization.Serialize(right));
    private static InvalidOperationException Missing() => new(RemoteTransferDistinctProtocol.Missing);
}
