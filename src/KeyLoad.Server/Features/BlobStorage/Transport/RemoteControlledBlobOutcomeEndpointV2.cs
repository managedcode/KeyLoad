using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class RemoteControlledBlobOutcomeEndpointV2
{
    internal static void RequireEnvelope(RemoteModelNativeRequest request, RemoteControlledBlobOutcomeCallV2 call)
    {
        if (request.Channel != RemoteModelNativeChannel.ResourceLifetimeBlobOutcome
            || call?.Original is not { } original || original.Request?.Frame is not { Purpose: ControlledBlobReadPurpose.Outcome } frame
            || original.Source is null || original.Destination?.Owner is null
            || !PhysicalOwnerEntryValidation.SameOwner(original.Source, request.SourceOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(original.Destination.Owner, request.DestinationOwner)
            || frame.ExpiresAt != request.ExpiresAt
            || request.CallerVoter != original.CallerVoter
            || request.CallerSiloAddress != original.CallerSiloAddress)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
    }

    internal static async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(RemoteControlledBlobOutcomeCallV2 call,
        RemoteControlledBlobReceiver receiver, NodeOptions options,
        PartitionMovementReplayCache replay, TimeProvider clock,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeEffectBoundary? boundary, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        receiver.ValidateOutcomeV2(call, token);
        if (!replay.TryUse(call.Original.Nonce))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        var remaining = call.Original.Request.Frame.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.Unavailable);
        }
        using var expiry = new CancellationTokenSource(remaining, clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
        RemoteControlledBlobOutcomeReplyV2 reply;
        try
        {
            var result = await receiver.ReadOutcomeV2Async(call, boundary, original.Token, binding).ConfigureAwait(false);
            reply = new(call.Original.RequestId, call.Original.Nonce, result, null, null, receiver.Discovery());
        }
        catch (global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeTerminalException terminal)
            when (terminal.Original is { } observedOriginal)
        {
            reply = new(call.Original.RequestId, call.Original.Nonce, null, observedOriginal.Code, observedOriginal.Message, receiver.Discovery());
        }
        catch (KeyLoadException error) when (boundary?.IsStarted == true)
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteDocumentProtocol.Unavailable, error);
        }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        {
            reply = new(call.Original.RequestId, call.Original.Nonce, null, error.Code, error.Message, receiver.Discovery());
        }
        var bytes = RemoteDocumentWire.Encode(reply, call.Original.MaximumReplyBytes);
        using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        original.Token.ThrowIfCancellationRequested();
        return (bytes, mac.Sign(bytes, reply: true));
    }
}
