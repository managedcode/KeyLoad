using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Owns bounded active native request producers and verified capability frames for one silo.</summary>
public sealed class NativeRequestWorkOwner : IAsyncDisposable
{
    private const int GetIsJoinedEmptyRead = 0;
    private const int Joined = 1;

    private readonly Lock gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly CancellationToken shutdownToken;
    private readonly NativeRequestWorkAdmissions admissions;
    private Task? drainTask;
    private Task? disposeTask;
    private bool admissionClosed;
    private int isJoined;

    /// <summary>Creates an empty silo-local work registry.</summary>
    /// <param name="options">The centrally validated request and capability admission limits.</param>
    public NativeRequestWorkOwner(IOptions<GrainRoutingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        admissions = new(options);
        shutdownToken = shutdown.Token;
    }

    /// <summary>Gets the owner cancellation token linked by admitted work.</summary>
    internal CancellationToken ShutdownToken => shutdownToken;

    /// <summary>Gets whether cancellation and every originally admitted frame have settled.</summary>
    internal bool IsJoined => Volatile.Read(ref isJoined) != GetIsJoinedEmptyRead;

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
            return admissions.Acquire(this, requestId, kind);
        }
    }

    /// <summary>Closes admission, cancels work, and returns the shared original drain task.</summary>
    /// <returns>The same task to every caller.</returns>
    internal Task DrainAsync()
        => NativeRequestWorkDrain.Start(gate, ref drainTask, ref admissionClosed, shutdown, admissions, this);

    internal void MarkJoined() => Volatile.Write(ref isJoined, Joined);

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
            task = DisposeCoreAsync(start.Task);
            disposeTask = task;
        }

        start.TrySetResult();
        return task;
    }

    private async Task DisposeCoreAsync(Task start)
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
        lock (gate)
        {
            admissions.Release(lease);
        }
    }

    internal Task PrepareCompletionAsync(NativeRequestWorkLease lease)
        => NativeRequestWorkCompletion.PrepareAsync(gate, lease);

    internal Task<NativeRequestWorkOriginalSettlement> CompleteAsync(NativeRequestWorkLease lease,
        Exception? original, Exception? cleanup)
        => NativeRequestWorkCompletion.CompleteAsync(gate, lease, original, cleanup);

    internal bool TryBeginUnbound(NativeRequestWorkLease lease)
        => NativeRequestWorkCompletion.TryBeginUnbound(gate, lease);

    internal void PrepareDisposal(NativeRequestWorkLease lease, Exception? original, Exception? cleanup)
        => NativeRequestWorkCompletion.PrepareDisposal(gate, lease, original, cleanup);

    internal void Bind(NativeRequestWorkLease lease, ImmutableArray<ResourceLifetimeV1> lifetimes)
    {
        lock (gate)
        {
            admissions.Bind(lease, lifetimes);
        }
    }

    internal void AdmitPublication(NativeRequestWorkLease lease)
    {
        lock (gate)
        {
            admissions.AdmitPublication(lease);
        }
    }

}
