using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Owns active execution request producers and verified capability frames for one silo.</summary>
public sealed class RequestWorkOwner : IAsyncDisposable
{
    private const int GetIsJoinedEmptyRead = 0;
    private const int Joined = 1;

    private readonly Lock gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly CancellationToken shutdownToken;
    private readonly RequestWorkAdmissions admissions;
    private Task? drainTask;
    private Task? disposeTask;
    private bool admissionClosed;
    private int isJoined;

    /// <summary>Creates an empty silo-local work registry.</summary>
    public RequestWorkOwner()
    {
        admissions = new();
        shutdownToken = shutdown.Token;
    }

    /// <summary>Gets the owner cancellation token linked by admitted work.</summary>
    internal CancellationToken ShutdownToken => shutdownToken;

    /// <summary>Gets whether cancellation and every originally admitted frame have settled.</summary>
    internal bool IsJoined => Volatile.Read(ref isJoined) != GetIsJoinedEmptyRead;

    /// <summary>Admits one unique request frame while the owner is open.</summary>
    /// <param name="requestId">The execution request identity.</param>
    /// <param name="kind">The closed frame class.</param>
    /// <returns>The lease which releases this exact admission.</returns>
    internal RequestWorkLease Acquire(Guid requestId, RequestWorkKind kind)
    {
        if (requestId == Guid.Empty || !Enum.IsDefined(kind))
        {
            throw Errors.Fail(ErrorCode.Validation, RequestWorkLimits.InvalidIdentityMessage);
        }
        lock (gate)
        {
            if (admissionClosed)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, RequestWorkLimits.AdmissionClosedMessage);
            }
            return admissions.Acquire(this, requestId, kind);
        }
    }

    /// <summary>Closes admission, cancels work, and returns the shared original drain task.</summary>
    /// <returns>The same task to every caller.</returns>
    internal Task DrainAsync()
        => RequestWorkDrain.Start(gate, ref drainTask, ref admissionClosed, shutdown, admissions, this);

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
        await start;
        var drainFailure = await RequestWorkSettlement.JoinAsync(DrainAsync());
        Exception? disposeFailure = null;
        try
        {
            shutdown.Dispose();
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            disposeFailure = error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            disposeFailure = error;
        }
        RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(drainFailure, disposeFailure));
    }

    internal void Release(RequestWorkLease lease)
    {
        lock (gate)
        {
            admissions.Release(lease);
        }
    }

    internal Task PrepareCompletionAsync(RequestWorkLease lease)
        => RequestWorkCompletion.PrepareAsync(gate, lease);

    internal Task<RequestWorkOriginalSettlement> CompleteAsync(RequestWorkLease lease,
        Exception? original, Exception? cleanup)
        => RequestWorkCompletion.CompleteAsync(gate, lease, original, cleanup);

    internal bool TryBeginUnbound(RequestWorkLease lease)
        => RequestWorkCompletion.TryBeginUnbound(gate, lease);

    internal void PrepareDisposal(RequestWorkLease lease, Exception? original, Exception? cleanup)
        => RequestWorkCompletion.PrepareDisposal(gate, lease, original, cleanup);

    internal void Bind(RequestWorkLease lease, ImmutableArray<ResourceLifetime> lifetimes)
    {
        lock (gate)
        {
            admissions.Bind(lease, lifetimes);
        }
    }

    internal void AdmitPublication(RequestWorkLease lease)
    {
        lock (gate)
        {
            admissions.AdmitPublication(lease);
        }
    }

}
