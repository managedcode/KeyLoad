using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeControlledBlobBorrowObservation
{
    private const int NoRecordedFailures = 0;

    internal static async Task ObserveAsync(INativeControlledBlobBorrowObserver? observer,
        RemoteModelNativeClient client, RegisteredPhysicalOwnerV1 destination, SiloAddress actualAddress,
        RemoteModelNativeRequest original, RemoteDocumentMac borrowedSourceMac,
        long maximumReplyBytes, CancellationToken originalToken)
    {
        if (observer is null)
        {
            return;
        }
        if (original.Channel == RemoteModelNativeChannel.ResourceLifetimeRead)
        {
            var call = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(original.Payload.Span);
            if (call.Kind != RemoteResourceLifetimeReadKindV2.ControlledBlob)
            {
                return;
            }
            originalToken.ThrowIfCancellationRequested();
            RemoteResourceLifetimeReadValidation.RequireEnvelope(original, call);
            RequireCanonical(original, RemoteDocumentWire.Encode(call), borrowedSourceMac);
            if (!observer.Select(call, originalToken))
            {
                return;
            }
            await ObserveSelectedAsync(client, destination, actualAddress, original, borrowedSourceMac,
                maximumReplyBytes,
                dispatcher => new(call, original.Signature, dispatcher, originalToken),
                catalog => observer.ObserveAsync(call, original, catalog, originalToken), originalToken).ConfigureAwait(false);
            return;
        }
        if (original.Channel != RemoteModelNativeChannel.ResourceLifetimeBlobOutcome)
        {
            return;
        }
        originalToken.ThrowIfCancellationRequested();
        var outcome = NativeSerialization.Deserialize<RemoteControlledBlobOutcomeCallV2>(original.Payload.Span);
        RemoteControlledBlobOutcomeEndpointV2.RequireEnvelope(original, outcome);
        RequireCanonical(original, RemoteDocumentWire.Encode(outcome), borrowedSourceMac);
        if (!observer.Select(outcome, originalToken))
        {
            return;
        }
        await ObserveSelectedAsync(client, destination, actualAddress, original, borrowedSourceMac,
            maximumReplyBytes,
            dispatcher => new(outcome, original.Signature, dispatcher, originalToken),
            catalog => observer.ObserveAsync(outcome, original, catalog, originalToken), originalToken).ConfigureAwait(false);
    }

    private static void RequireCanonical(RemoteModelNativeRequest original, byte[] canonical, RemoteDocumentMac mac)
    {
        if (!mac.Verify(original.Payload.Span, original.Signature, reply: false)
            || !original.Payload.Span.SequenceEqual(canonical))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
        }
    }

    private static async Task ObserveSelectedAsync(RemoteModelNativeClient client,
        RegisteredPhysicalOwnerV1 destination, SiloAddress actualAddress, RemoteModelNativeRequest original,
        RemoteDocumentMac borrowedSourceMac, long maximumReplyBytes,
        Func<NativeControlledBlobFaultDispatcher, NativeControlledBlobBorrowedCatalog> createCatalog,
        Func<NativeControlledBlobBorrowedCatalog, ValueTask> observe, CancellationToken originalToken)
    {
        var dispatcher = new NativeControlledBlobFaultDispatcher(client, destination, actualAddress,
            original, borrowedSourceMac, maximumReplyBytes, originalToken);
        var failures = new List<Exception>();
        try
        {
            NativeControlledBlobBorrowedCatalog catalog;
            await using ((catalog = createCatalog(dispatcher)).ConfigureAwait(false))
            {
                await ServerFailureObserver.ObserveAsync(() => observe(catalog).AsTask(), failures).ConfigureAwait(false);
            }
            if (failures.Count == NoRecordedFailures)
            {
                catalog.RequireComplete();
            }
        }
        catch (Exception error) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
        await client.RequireBeforeDispatchAsync(destination, original, borrowedSourceMac, originalToken).ConfigureAwait(false);
    }
}
