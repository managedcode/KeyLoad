using KeyLoad.Clustering;
using KeyLoad.Server.Features.BlobStorage;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeObservedExchange
{
    internal static async Task<(RemoteModelNativeReply Reply, bool PriorReplayEffects)> ExecuteAsync(RemoteModelNativeClient client, RegisteredPhysicalOwnerV1 destination,
        SiloAddress address, RemoteModelNativeRequest request, RemoteDocumentMac sourceMac,
        long maximumReplyBytes, INativeControlledBlobBorrowObserver? blobObserver,
        IRemoteModelNativeReplayObserver? replayObserver, IRemoteModelNativeFaultObserver? faultObserver, CancellationToken token)
    {
        var replayEffects = false;
        try
        {
            await NativeControlledBlobBorrowObservation.ObserveAsync(blobObserver, client, destination, address,
                request, sourceMac, maximumReplyBytes, token).ConfigureAwait(false);
            if (replayObserver?.Select(request, token) is { } mode)
            {
                var replay = new RemoteModelNativeReplayExchange(client, destination, request, maximumReplyBytes, mode, token);
                try
                {
                    await using var ownedReplay = replay;
                    await replayObserver.ObserveAsync(request, replay, token).ConfigureAwait(false);
                    await client.RequireBeforeDispatchAsync(destination, request, sourceMac, token).ConfigureAwait(false);
                }
                finally
                {
                    replayEffects |= replay.InvokedEffects;
                }
            }
            if (faultObserver is not null)
            {
                await faultObserver.BeforeDispatchAsync(request, token).ConfigureAwait(false);
                await client.RequireBeforeDispatchAsync(destination, request, sourceMac, token).ConfigureAwait(false);
            }
            return (await client.DispatchAsync(address, request, token).ConfigureAwait(false), replayEffects);
        }
        catch (Exception error) when (replayEffects && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error);
        }
    }

    internal static RemoteModelAuthenticatedReply Complete(RemoteModelNativeReply reply, RemoteModelNativeRequest request,
        bool effects, bool priorReplayEffects)
    {
        // A correctly authenticated receiver refusal is interpreted only after exact dispatch identity checks.
        if (reply.Error is { } failure)
        {
            var originalFailure = Errors.Fail(failure, RemoteModelNativeProtocol.Unavailable);
            if (priorReplayEffects)
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, originalFailure);
            }
            throw originalFailure;
        }
        return new(reply.Payload, reply.Signature, request.DestinationSiloAddress)
        {
            MayHaveEffects = effects
        };
    }
}
