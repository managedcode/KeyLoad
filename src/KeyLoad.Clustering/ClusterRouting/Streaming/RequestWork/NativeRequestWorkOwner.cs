using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Owns bounded active native request producers and verified capability frames for one silo.</summary>
public sealed class NativeRequestWorkOwner : IAsyncDisposable, IResourceLifetimeWorkFence
{
    private const int GetIsJoinedEmptyRead = 0;
    private const int Joined = 1;

    private readonly Lock gate = new();
    private readonly NativeResourceWorkPhysicalBinding physical = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly CancellationToken shutdownToken;
    private readonly NativeRequestWorkAdmissions admissions;
    private readonly NativeResourceWorkRetentions retentions;
    private readonly NativeRequestWorkChildren children;
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
        retentions = new(options);
        children = new(gate, this, physical, admissions, retentions, () => admissionClosed);
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
            retentions.RequireAdmissionSlot(admissions.Count);
            return admissions.Acquire(this, requestId, kind);
        }
    }

    internal NativeRequestWorkLease RequireOriginalParentBinding(NativeRequestWorkLease child)
        => children.RequireOriginalParentBinding(child);

    internal NativeRequestWorkChildReservation ReserveLocalChild(NativeRequestWorkLease parent,
        DecodedGrainRequest verified, PhysicalShardRecord expectedOwner, SiloStorageIdentity expectedRuntime)
        => children.Reserve(parent, verified, expectedOwner, expectedRuntime);

    internal NativeRequestWorkLease AcquireVerifiedProducer(DecodedGrainRequest verified,
        PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime)
        => children.AcquireVerifiedProducer(verified, actualOwner, actualRuntime);

    internal bool BeginChildDispatchCompletion(NativeRequestWorkChildReservation reservation)
        => children.BeginCompletion(reservation);

    internal void RecordChildCleanup(NativeRequestWorkLease parent, ImmutableArray<Exception> failures)
        => children.RecordCleanup(parent, failures);

    /// <summary>Closes admission, cancels work, and returns the shared original drain task.</summary>
    /// <returns>The same task to every caller.</returns>
    internal Task DrainAsync()
        => NativeRequestWorkDrain.Start(gate, ref drainTask, ref admissionClosed, shutdown, admissions, retentions, this);

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

    internal void BindPhysicalOwner(PhysicalShardRecord actualOwner)
    {
        lock (gate)
        {
            physical.Bind(actualOwner);
        }
    }

    internal void AttachResourceLifetimeWorkFence(DatabaseEngine sameDatabase, long originalDurableLogLastIndex)
    {
        var startup = NativeResourceWorkPhysicalBinding.CaptureStartup(sameDatabase, originalDurableLogLastIndex);
        lock (gate)
        {
            physical.Attach(sameDatabase, this, startup.Applied, startup.NodeId, startup.Generation);
        }
    }

    IResourceLifetimeWorkSettlement IResourceLifetimeWorkFence.CloseMatching(ResourceRetirementOwnerFenceV1 fence)
        => CloseMatching(fence);

    internal IResourceLifetimeWorkSettlement CloseMatching(ResourceRetirementOwnerFenceV1 fence)
    {
        var fingerprint = NativeResourceWorkRetentions.Fingerprint(fence);
        lock (gate)
        {
            var actual = physical.Require(fence);
            return retentions.CloseMatching(this, fence, fingerprint, actual, admissions, physical.CanQualify(fence), retainCancellation: !admissionClosed);
        }
    }

    internal NativeResourceWorkClosure? FindRetainedSettlement(ResourceRetirementOwnerFenceV1 fence)
    {
        lock (gate)
        {
            _ = physical.Require(fence);
            return physical.CanQualify(fence) ? retentions.Find(fence) : null;
        }
    }

    internal void ConsumeSettlement(ResourceRetirementOwnerFenceV1 fence, NativeResourceWorkClosure originalHandle)
    {
        var fingerprint = NativeResourceWorkRetentions.Fingerprint(fence);
        lock (gate)
        {
            retentions.Consume(fence, fingerprint, originalHandle, admissions);
        }
    }

    internal void ReleaseCloseObserver(NativeRequestWorkLease lease)
        => NativeRequestWorkCompletion.ReleaseCloseObserver(gate, lease);

}
