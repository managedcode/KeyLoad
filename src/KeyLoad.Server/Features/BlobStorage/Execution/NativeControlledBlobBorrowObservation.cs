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
        if (observer is null || original.Channel != RemoteModelNativeChannel.DocumentEnvelope)
        { return; }
        originalToken.ThrowIfCancellationRequested();
        var envelope = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(original.Payload.Span);
        if (envelope.ControlledBlob is not { } call)
        { return; }
        if (envelope.Document is not null || envelope.Controlled is not null || envelope.QueueTransfer is not null
            || !borrowedSourceMac.Verify(original.Payload.Span, original.Signature, reply: false)
            || !original.Payload.Span.SequenceEqual(RemoteDocumentWire.Encode(envelope).AsSpan()))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        if (!observer.Select(call, originalToken))
        { return; }
        var dispatcher = new NativeControlledBlobFaultDispatcher(client, destination, actualAddress,
            original, borrowedSourceMac, maximumReplyBytes, originalToken);
        var failures = new List<Exception>();
        try
        {
            NativeControlledBlobBorrowedCatalog catalog;
            await using ((catalog = new NativeControlledBlobBorrowedCatalog(
                call, original.Signature, dispatcher, originalToken)).ConfigureAwait(false))
            {
                await ServerFailureObserver.ObserveAsync(
                    () => observer.ObserveAsync(call, original, catalog, originalToken).AsTask(), failures).ConfigureAwait(false);
            }
            if (failures.Count == NoRecordedFailures)
            { catalog.RequireComplete(); }
        }
        catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
        await client.RequireBeforeDispatchAsync(destination, original, borrowedSourceMac, originalToken).ConfigureAwait(false);
    }
}
