using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Owns bounded active native request producers and verified capability frames for one silo.</summary>
public sealed class NativeRequestWorkOwner : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly CancellationToken shutdownToken;
    private readonly Dictionary<(Guid RequestId, NativeRequestWorkKind Kind), NativeRequestWorkLease> active = [];
    private TaskCompletionSource? zeroFrames;
    private Task? drainTask;
    private Task? disposeTask;
    private int requestProducers;
    private bool admissionClosed;
    private int isJoined;
    private readonly GrainRoutingOptions settings;

    /// <summary>Creates an empty silo-local work registry.</summary>
    /// <param name="options">The centrally validated request and capability admission limits.</param>
    public NativeRequestWorkOwner(IOptions<GrainRoutingOptions> options)
    {
        settings = options.Value;
        shutdownToken = shutdown.Token;
    }

    /// <summary>Gets the owner cancellation token linked by admitted work.</summary>
    internal CancellationToken ShutdownToken => shutdownToken;

    /// <summary>Gets whether cancellation and every originally admitted frame have settled.</summary>
    internal bool IsJoined => Volatile.Read(ref isJoined) != 0;

    /// <summary>Admits one unique request frame while the owner is open.</summary>
    /// <param name="requestId">The native request identity.</param>
    /// <param name="kind">The closed frame class.</param>
    /// <returns>The lease which releases this exact admission.</returns>
    internal NativeRequestWorkLease Acquire(Guid requestId, NativeRequestWorkKind kind)
    {
        if (requestId == Guid.Empty || !Enum.IsDefined(kind))
        {
            throw Errors.Fail(ErrorCode.Validation, NativeRequestWorkLimits.InvalidIdentityMessage);
        }

        lock (gate)
        {
            if (admissionClosed)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.AdmissionClosedMessage);
            }

            var key = (requestId, kind);
            if (active.ContainsKey(key) || active.Count >= settings.MaximumTotalFrames
                || kind == NativeRequestWorkKind.RequestProducer
                && requestProducers >= settings.MaximumRequestProducers)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, NativeRequestWorkLimits.CapacityMessage);
            }

            if (active.Count == 0)
            {
                zeroFrames = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            var lease = new NativeRequestWorkLease(this, requestId, kind);
            active.Add(key, lease);
            if (kind == NativeRequestWorkKind.RequestProducer)
            {
                requestProducers++;
            }

            return lease;
        }
    }

    /// <summary>Closes admission, cancels work, and returns the shared original drain task.</summary>
    /// <returns>The same task to every caller.</returns>
    internal Task DrainAsync()
    {
        TaskCompletionSource start;
        Task task;
        lock (gate)
        {
            if (drainTask is not null)
            {
                return drainTask;
            }

            admissionClosed = true;
            var frames = zeroFrames?.Task ?? Task.CompletedTask;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = CompleteDrainAsync(start.Task, frames);
            drainTask = task;
        }

        start.TrySetResult();
        return task;
    }

    /// <summary>Returns the one disposal task through the asynchronous-disposal contract.</summary>
    public ValueTask DisposeAsync() => new(GetDisposeTask());

    private Task GetDisposeTask()
    {
        TaskCompletionSource start;
        Task task;
        lock (gate)
        {
            if (disposeTask is not null)
            {
                return disposeTask;
            }

            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = CompleteDisposeAsync(start.Task);
            disposeTask = task;
        }

        start.TrySetResult();
        return task;
    }

    private async Task CompleteDrainAsync(Task start, Task frames)
    {
        await start.ConfigureAwait(false);
        var cancelFailure = await NativeRequestWorkSettlement.CancelAsync(shutdown).ConfigureAwait(false);
        var frameFailure = await NativeRequestWorkSettlement.JoinAsync(frames).ConfigureAwait(false);
        Volatile.Write(ref isJoined, 1);
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(cancelFailure, frameFailure));
    }

    private async Task CompleteDisposeAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var drainFailure = await NativeRequestWorkSettlement.JoinAsync(DrainAsync()).ConfigureAwait(false);
        Exception? disposeFailure = null;
        try
        {
            shutdown.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            disposeFailure = error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            disposeFailure = error;
        }

        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(drainFailure, disposeFailure));
    }

    internal void Release(NativeRequestWorkLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        TaskCompletionSource? drained = null;
        lock (gate)
        {
            var key = (lease.RequestId, lease.Kind);
            if (!active.TryGetValue(key, out var registered) || !ReferenceEquals(registered, lease))
            {
                throw Errors.Fail(ErrorCode.Corruption, NativeRequestWorkLimits.LeaseIdentityMessage);
            }

            active.Remove(key);
            if (lease.Kind == NativeRequestWorkKind.RequestProducer)
            {
                requestProducers--;
            }

            if (active.Count == 0)
            {
                drained = zeroFrames;
                zeroFrames = null;
            }
        }

        drained?.TrySetResult();
    }
}
