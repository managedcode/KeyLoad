using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeControlledBlobBorrowedCatalog : IAsyncDisposable
{
    private const int NoOptionalFrame = 0;
    private const int PresentOptionalFrame = 1;
    private readonly Lock gate = new();
    private readonly IEnumerator<NativeControlledBlobFaultPayload> catalog;
    private readonly NativeControlledBlobFaultDispatcher dispatcher;
    private readonly CancellationToken originalToken;
    private readonly int expected;
    private readonly List<Exception> actualFailures = new(NativeBlobWireProbeProtocol.MaximumFaultCount);
    private Task<NativeControlledBlobFaultResult>? running;
    private Task? closing;
    private bool active;
    private int count;

    internal NativeControlledBlobBorrowedCatalog(RemoteControlledBlobCall original, string signature,
        NativeControlledBlobFaultDispatcher dispatcher, CancellationToken originalToken)
    {
        this.dispatcher = dispatcher;
        this.originalToken = originalToken;
        expected = NativeBlobWireProbeProtocol.CommonFaultCount + (original.Request.Frame.OriginalOutcome is null ? NoOptionalFrame : PresentOptionalFrame)
            + (original.Request.Frame.Original is null ? NoOptionalFrame : PresentOptionalFrame);
        catalog = NativeControlledBlobFaultCatalog.Create(original, signature, originalToken).GetEnumerator();
    }

    internal Task<NativeControlledBlobFaultResult> ExecuteNextAsync()
    {
        TaskCompletionSource start;
        Task<NativeControlledBlobFaultResult> admitted;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing is not null, this);
            if (active || count >= expected) { throw new InvalidOperationException(RemoteModelNativeProtocol.InvalidProof); }
            originalToken.ThrowIfCancellationRequested();
            if (!catalog.MoveNext()) { throw new InvalidOperationException(RemoteModelNativeProtocol.InvalidProof); }
            var payload = catalog.Current;
            active = true;
            count++;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            running = RunAsync(start.Task, payload, count);
            admitted = running;
        }
        start.SetResult();
        return admitted;
    }

    internal void RequireComplete()
    {
        lock (gate)
        {
            originalToken.ThrowIfCancellationRequested();
            if (active || count != expected)
            { throw new InvalidOperationException(RemoteModelNativeProtocol.InvalidProof); }
        }
    }

    private async Task<NativeControlledBlobFaultResult> RunAsync(Task start,
        NativeControlledBlobFaultPayload payload, int ordinal)
    {
        await start.ConfigureAwait(false);
        try { return await dispatcher.ExecuteAsync(payload, ordinal).ConfigureAwait(false); }
        catch (Exception error) { lock (gate) { actualFailures.Add(error); } throw; }
        finally { lock (gate) { active = false; } }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) { return new(closing ??= JoinAsync(running)); }
    }

    private async Task JoinAsync(Task? admitted)
    {
        var joinFailures = new List<Exception>();
        if (admitted is not null)
        { await ServerFailureObserver.ObserveAsync(() => admitted, joinFailures).ConfigureAwait(false); }
        List<Exception> failures;
        lock (gate) { failures = new(actualFailures); }
        foreach (var error in joinFailures)
        { if (!failures.Any(existing => ReferenceEquals(existing, error))) { failures.Add(error); } }
        try { catalog.Dispose(); }
        catch (Exception error) { failures.Add(error); throw; }
        finally { ServerFailureObserver.ThrowIfAny(failures); }
    }
}
