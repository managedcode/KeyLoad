using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireProbeObserver(NativeBlobWireSelection selection,
    NativeBlobWireReservations reservations, NativeBlobWireEvidence evidence) : INativeControlledBlobBorrowObserver, IAsyncDisposable
{
    private const int FirstFaultOrdinal = 1;
    private readonly NativeBlobWireCallbackLifetime lifetime = new();

    public bool Select(RemoteControlledBlobCall original, CancellationToken originalCancellationToken)
    {
        lifetime.Enter();
        try
        { return selection.Select(original, originalCancellationToken); }
        finally { lifetime.Leave(); }
    }

    public async ValueTask ObserveAsync(RemoteControlledBlobCall original, RemoteModelNativeRequest signedOriginal,
        NativeControlledBlobBorrowedCatalog borrowedCatalog, CancellationToken originalCancellationToken)
    {
        lifetime.Enter();
        var failures = new List<Exception>();
        NativeBlobWireReservation? reservation = null;
        try
        {
            var selected = selection.RequireSelected(original, originalCancellationToken);
            if (signedOriginal.CallerVoter != selected.Claim.CallerVoter
                || signedOriginal.CallerSiloAddress != selected.Claim.CallerSiloAddress
                || signedOriginal.ExpiresAt != selected.Claim.ExpiresAt)
            { throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidEvidence); }
            reservation = reservations.Enter(selected.Snapshot.Owner, selected.Snapshot.OwnerDigest, selected.Claim, selected.Digest);
            var count = original.Request.Frame.Original is null
                ? NativeBlobWireProbeProtocol.CommonFaultCount : NativeBlobWireProbeProtocol.MaximumFaultCount;
            var originalBytes = evidence.EncodeNative(signedOriginal);
            var results = new NativeBlobWireResultEntry[count];
            for (var ordinal = FirstFaultOrdinal; ordinal <= count; ordinal++)
            {
                var actual = await borrowedCatalog.ExecuteNextAsync().ConfigureAwait(false);
                NativeBlobWireRefusal.Require(signedOriginal, actual);
                results[ordinal - FirstFaultOrdinal] = evidence.Write(selected.Claim, selected.Digest,
                    originalBytes, actual, ordinal, originalCancellationToken);
            }
            evidence.Complete(selected.Claim, selected.Digest, results, originalCancellationToken);
        }
        catch (Exception error) { failures.Add(error); throw; }
        finally
        {
            if (reservation is not null)
            { ServerFailureObserver.Observe(() => reservations.Leave(reservation), failures); }
            ServerFailureObserver.Observe(lifetime.Leave, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal Task StopAsync() => lifetime.CloseAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
}
