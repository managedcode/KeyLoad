using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Owns an original admission, its selective token, and its original async settlement.</summary>
internal sealed class NativeRequestWorkLease : IDisposable, INativeRequestWorkBinding
{
    private readonly NativeRequestWorkOwner owner;
    private ResourceLifetimeWorkResultRetention? resultRetention;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource<NativeRequestWorkOriginalSettlement> settlement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? cancelObservers;
    private int observerCount;
    private bool publicationClosed;
    private readonly Dictionary<ResourceLifetimeV1, NativeResourceWorkFenceAdmission> retirements = [];
    internal NativeRequestWorkChildReservation? ChildReservation
    {
        get; set;
    }
    internal NativeCommandObservation? CommandObservation
    {
        get; set;
    }
    public NativeRequestWorkLease OriginalProducer => this;
    internal bool OwnsResultRetention => ChildReservation is null;
    internal ImmutableArray<Exception> ChildCleanupFailures { get; set; } = [];
    internal bool HasRetirementHolds => retirements.Count != NoRetirements;
    private const int NoRetirements = 0;
    internal bool Completing
    {
        get; private set;
    }
    internal Task? CompletionDriver
    {
        get; set;
    }
    private const int NoObservers = 0;
    private const int SingleObserver = 1;

    internal NativeRequestWorkLease(NativeRequestWorkOwner owner, Guid id, NativeRequestWorkKind kind, long generation)
    {
        this.owner = owner;
        RequestId = id;
        Kind = kind;
        AdmissionGeneration = generation;
        Cancellation = cancellation.Token;
    }

    internal Guid RequestId
    {
        get;
    }
    internal NativeRequestWorkKind Kind
    {
        get;
    }
    internal long AdmissionGeneration
    {
        get;
    }
    internal long LastPublicationGeneration
    {
        get; set;
    }
    internal CancellationToken Cancellation
    {
        get;
    }
    internal Task<NativeRequestWorkOriginalSettlement> Settlement => settlement.Task;
    internal ImmutableHashSet<ResourceLifetimeV1> Dependencies { get; set; } = [];
    public ResourceLifetimeWorkResultRetention ResultRetention => Volatile.Read(ref resultRetention)
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
    internal void AttachResultRetention(ResourceLifetimeWorkResultRetention original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var existing = Interlocked.CompareExchange(ref resultRetention, original, null);
        if (existing is not null && !ReferenceEquals(existing, original))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }
    public void Bind(ResourceLifetimeCoverageV1 coverage) => Bind(coverage.Lifetimes);
    public void Bind(ImmutableArray<ResourceLifetimeV1> actualLifetimes)
    {
        ResultRetention.Reserve(actualLifetimes);
        owner.Bind(this, actualLifetimes);
    }
    internal NativeRequestWorkLease RequireOriginalParentBinding()
        => owner.RequireOriginalParentBinding(this);

    internal NativeRequestWorkChildReservation ReserveLocalChild(DecodedGrainRequest verified,
        PhysicalShardRecord expectedOwner, ReplicaSiloDiscovery expectedRuntime)
        => owner.ReserveLocalChild(this, verified, expectedOwner, expectedRuntime);

    internal void AdmitPublication() => owner.AdmitPublication(this);
    internal Task<Exception?> CancelAsync() => NativeRequestWorkSettlement.CancelAsync(cancellation);

    internal void MarkRetirement(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
    {
        if (retirements.TryGetValue(fence.Control.Lifetime, out var original))
        {
            NativeResourceWorkRetentions.RequireFingerprint(original.Fingerprint, fingerprint);
            return;
        }
        publicationClosed = true;
        retirements.Add(fence.Control.Lifetime, new(fingerprint, Snapshot()));
    }

    internal NativeResourceWorkSnapshot RetirementSnapshot(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
    {
        var original = retirements[fence.Control.Lifetime];
        NativeResourceWorkRetentions.RequireFingerprint(original.Fingerprint, fingerprint);
        return original.Snapshot;
    }

    internal void ConsumeRetirement(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
    {
        _ = RetirementSnapshot(fence, fingerprint);
        retirements.Remove(fence.Control.Lifetime);
    }

    internal bool ClosePublication(bool retainCancellation)
    {
        publicationClosed = true;
        if (Completing || !retainCancellation)
        {
            return false;
        }
        if (observerCount == NoObservers)
        {
            cancelObservers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        observerCount = checked(observerCount + SingleObserver);
        return true;
    }

    internal void RetainCompletionObserver()
    {
        RequirePublicationOpen();
        if (observerCount == NoObservers)
        {
            cancelObservers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        observerCount = checked(observerCount + SingleObserver);
    }

    internal Task BeginCompletion()
    {
        Completing = true;
        publicationClosed = true;
        return cancelObservers?.Task ?? Task.CompletedTask;
    }

    internal TaskCompletionSource? ReleaseCloseObserver()
        => --observerCount == NoObservers ? cancelObservers : null;

    internal void RequirePublicationOpen()
    {
        if (publicationClosed || Completing)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.AdmissionClosedMessage);
        }
    }

    internal Task PrepareCompletionAsync() => owner.PrepareCompletionAsync(this);

    internal Task<NativeRequestWorkOriginalSettlement> CompleteAsync(Exception? original, Exception? cleanup)
        => owner.CompleteAsync(this, original, cleanup);

    internal async Task FinishAsync(Task start, Task observers, Exception? original, Exception? cleanup)
    {
        await start.ConfigureAwait(false);
        await observers.ConfigureAwait(false);
        Finish(original, cleanup);
    }

    internal NativeRequestWorkOriginalSettlement Finish(Exception? original, Exception? cleanup)
    {
        var failures = ImmutableArray.CreateBuilder<Exception>();
        Add(cleanup, failures);
        failures.AddRange(ChildCleanupFailures);
        Add(DisposeCancellation(), failures);
        Add(ReleaseAdmission(), failures);
        var outcome = new NativeRequestWorkOriginalSettlement(original, failures.ToImmutable());
        Volatile.Write(ref resultRetention, null);
        settlement.TrySetResult(outcome);
        return outcome;
    }

    private static void Add(Exception? error, ImmutableArray<Exception>.Builder failures)
    {
        if (error is not null)
        {
            failures.Add(error);
        }
    }

    private Exception? DisposeCancellation()
    {
        try
        {
            cancellation.Dispose();
            return null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    private Exception? ReleaseAdmission()
    {
        try
        {
            owner.Release(this);
            return null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    internal NativeResourceWorkSnapshot Snapshot()
        => new(RequestId, Kind, AdmissionGeneration, LastPublicationGeneration);
    public void Dispose() => owner.CompleteUnbound(this);
}

internal sealed record NativeRequestWorkOriginalSettlement(Exception? OriginalFailure, ImmutableArray<Exception> CleanupFailures)
{
    internal Exception? CleanupOrJoinFailure
    {
        get
        {
            Exception? failure = null;
            foreach (var cleanup in CleanupFailures)
            {
                failure = NativeRequestWorkSettlement.Preserve(failure, cleanup);
            }
            return failure;
        }
    }
}

internal sealed record NativeResourceWorkFenceAdmission(ImmutableArray<byte> Fingerprint, NativeResourceWorkSnapshot Snapshot);
