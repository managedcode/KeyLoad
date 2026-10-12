using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeReplayExecution
{
    private const int SelectedVoter = 0;
    private const long EmptyReplyBytes = 0;

    internal static async Task<RemoteModelNativeReplayResult> ExecuteAsync(RemoteModelNativeClient client,
        RegisteredPhysicalOwnerV1 destination,
        RemoteModelNativeRequest original, long maximumReplyBytes, RemoteModelNativeReplayVariation variation,
        RemoteModelNativeReplayExchange owner, NodeOptions options, PartitionHost partition,
        PartitionMovementGrainServiceClient movement, ReplicaSiloDiscoveryState local, TimeProvider clock,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.DestinationOwner != destination.Owner || original.ExpiresAt <= clock.GetUtcNow()
            || maximumReplyBytes is <= EmptyReplyBytes or > RemoteDocumentProtocol.MaximumBodyBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(RemoteModelNativeIdentity.Secret(options, partition, original.SourceOwner));
        if (!sourceMac.VerifyNativeRequest(original) || !sourceMac.Verify(original.Payload.Span, original.Signature, false))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        var payload = RemoteModelNativeReplayPayload.Create(original, variation);
        _ = RemoteModelNativeIdentity.Secret(options, partition, destination.Owner);
        var address = await movement.ResolveAsync(destination, SelectedVoter, token).ConfigureAwait(false);
        var current = local.Read();
        token.ThrowIfCancellationRequested();
        if (!current.TransportReady || !original.SourceOwner.VoterIds.Contains(current.VoterId, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable); }
        await movement.RequireCallerAsync(current.VoterId, current.SiloAddress, token).ConfigureAwait(false);
        var request = original with
        {
            Payload = payload.ToArray(),
            CallerVoter = current.VoterId,
            CallerSiloAddress = current.SiloAddress,
            DestinationVoter = destination.Owner.VoterIds[SelectedVoter],
            DestinationSiloAddress = address.ToParsableString(),
            Nonce = Guid.NewGuid(),
            SenderSignature = string.Empty
        };
        request = request with { SenderSignature = sourceMac.SignNativeRequest(request) };
        token.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteModelNativeProtocol.Unavailable); }
        owner.MarkInvoked(variation);
        var received = await client.DispatchAsync(address, request, token).ConfigureAwait(false);
        RemoteModelNativeReply reply;
        try
        { reply = await client.VerifyReplyAsync(destination, request, received, maximumReplyBytes, token).ConfigureAwait(false); }
        catch (Exception error) when (RemoteModelNativeEffects.MayHaveEffects(request.Channel, request.Payload)
            && NativeCqrsBoundaryErrors.IsNonFatal(error))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error); }
        // Preserve signed native terminal refusal as actual evidence; it is not an acknowledgement.
        return new(request, reply);
    }
}
