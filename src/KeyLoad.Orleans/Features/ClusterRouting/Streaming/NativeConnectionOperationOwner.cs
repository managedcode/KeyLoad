using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Owns bounded live operations using the connection activation's native turns.</summary>
internal sealed class NativeConnectionOperationOwner : IAsyncDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    private readonly HashSet<Guid> active = [];
    private readonly int maximumOperations;
    private TaskCompletionSource? drained;
    private Task? closing;
    private Task? disposal;
    private bool admissionClosed;

    internal NativeConnectionOperationOwner(IOptions<GrainRoutingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        maximumOperations = options.Value.MaximumConnectionOperations;
    }

    internal CancellationToken ShutdownToken => shutdown.Token;

    internal NativeConnectionOperationLease Acquire(Guid requestId)
    {
        if (requestId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, NativeRequestWorkLimits.InvalidIdentityMessage); }
        if (admissionClosed)
        { throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.AdmissionClosedMessage); }
        if (active.Count >= maximumOperations || active.Contains(requestId))
        { throw Errors.Fail(ErrorCode.ResourceExhausted, NativeRequestWorkLimits.CapacityMessage); }
        active.Add(requestId);
        return new(this, requestId);
    }

    internal Task CloseAsync()
    {
        if (closing is not null)
        { return closing; }
        admissionClosed = true;
        var originalOperations = Task.CompletedTask;
        if (active.Count != default(int))
        {
            drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            originalOperations = drained.Task;
        }
        return closing = CloseCoreAsync(originalOperations);
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task CloseCoreAsync(Task originalOperations)
    {
        var cancellationFailure = await NativeRequestWorkSettlement.CancelAsync(shutdown).ConfigureAwait(true);
        var operationFailure = await NativeRequestWorkSettlement.JoinAsync(originalOperations).ConfigureAwait(true);
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(cancellationFailure, operationFailure));
    }

    private async Task DisposeCoreAsync()
    {
        var closeFailure = await NativeRequestWorkSettlement.JoinAsync(CloseAsync()).ConfigureAwait(true);
        try
        { shutdown.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(closeFailure, error)); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(closeFailure, error)); }
        NativeRequestWorkSettlement.Rethrow(closeFailure);
    }

    internal void Release(Guid requestId)
    {
        if (!active.Remove(requestId))
        { throw Errors.Fail(ErrorCode.Corruption, NativeRequestWorkLimits.LeaseIdentityMessage); }
        if (active.Count == default(int))
        {
            var original = drained;
            drained = null;
            original?.TrySetResult();
        }
    }
}
