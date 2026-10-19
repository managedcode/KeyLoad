using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Owns bounded live operations using the connection activation's execution turns.</summary>
internal sealed class ConnectionOperationOwner : IAsyncDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    private readonly HashSet<Guid> active = [];
    private readonly int maximumOperations;
    private TaskCompletionSource? drained;
    private Task? closing;
    private Task? disposal;
    private bool admissionClosed;

    internal ConnectionOperationOwner(IOptions<GrainRoutingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        maximumOperations = options.Value.MaximumConnectionOperations;
    }

    internal CancellationToken ShutdownToken => shutdown.Token;

    internal void ObserveOriginalCancellation(OperationCanceledException original, Guid requestId,
        GrainFailureStage callSiteStage, Microsoft.Extensions.Logging.ILogger logger, CancellationToken writerToken)
    {
        var requestActive = active.Contains(requestId);
        var closed = admissionClosed;
        var ownerCancelled = shutdown.IsCancellationRequested;
        ConnectionCancellationDiagnostic.Observe(logger, original, requestId, callSiteStage,
            requestActive, closed, ownerCancelled, original.CancellationToken == writerToken);
    }

    internal ConnectionOperationLease Acquire(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, RequestWorkLimits.InvalidIdentityMessage);
        }
        if (admissionClosed)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RequestWorkLimits.AdmissionClosedMessage);
        }
        if (active.Count >= maximumOperations || active.Contains(requestId))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RequestWorkLimits.CapacityMessage);
        }
        active.Add(requestId);
        return new(this, requestId);
    }

    internal Task CloseAsync()
    {
        if (closing is not null)
        {
            return closing;
        }
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
        var cancellationFailure = await RequestWorkSettlement.CancelAsync(shutdown).ConfigureAwait(true);
        var operationFailure = await RequestWorkSettlement.JoinAsync(originalOperations).ConfigureAwait(true);
        RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(cancellationFailure, operationFailure));
    }

    private async Task DisposeCoreAsync()
    {
        var closeFailure = await RequestWorkSettlement.JoinAsync(CloseAsync()).ConfigureAwait(true);
        try
        {
            shutdown.Dispose();
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(closeFailure, error));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(closeFailure, error));
        }
        RequestWorkSettlement.Rethrow(closeFailure);
    }

    internal void Release(Guid requestId)
    {
        if (!active.Remove(requestId))
        {
            throw Errors.Fail(ErrorCode.Corruption, RequestWorkLimits.LeaseIdentityMessage);
        }
        if (active.Count == default(int))
        {
            var original = drained;
            drained = null;
            original?.TrySetResult();
        }
    }
}
