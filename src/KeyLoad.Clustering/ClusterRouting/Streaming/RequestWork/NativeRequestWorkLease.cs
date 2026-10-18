using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Owns an original admission, its selective token, and its original async settlement.</summary>
internal sealed class NativeRequestWorkLease : IDisposable, IAsyncDisposable, INativeRequestWorkBinding
{
    private readonly NativeRequestWorkOwner owner;
    private ResourceLifetimeWorkResultRetention? resultRetention;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource<NativeRequestWorkOriginalSettlement> settlement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NativeRequestWorkLeasePublication publication = new([]);
    private bool contextFrozen;
    private Exception? originalFailure;
    private Exception? cleanupFailure;
    internal NativeRequestWorkChildReservation? ChildReservation
    {
        get; set;
    }
    public NativeRequestWorkLease OriginalProducer => this;
    internal bool OwnsResultRetention => ChildReservation is null;
    internal ImmutableArray<Exception> ChildCleanupFailures { get; set; } = [];
    internal bool HasRetirementHolds => publication.HasRetirementHolds;
    internal bool Completing => publication.Completing;
    internal Task? CompletionDriver
    {
        get; set;
    }

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
        PhysicalShardRecord expectedOwner, SiloStorageIdentity expectedRuntime)
        => owner.ReserveLocalChild(this, verified, expectedOwner, expectedRuntime);

    internal void AdmitPublication() => owner.AdmitPublication(this);
    internal Task<Exception?> CancelAsync() => NativeRequestWorkSettlement.CancelAsync(cancellation);

    internal void MarkRetirement(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
        => publication.MarkRetirement(fence, fingerprint, this);

    internal NativeResourceWorkSnapshot RetirementSnapshot(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
        => publication.RetirementSnapshot(fence, fingerprint);

    internal void ConsumeRetirement(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
        => publication.ConsumeRetirement(fence, fingerprint);

    internal bool ClosePublication(bool retainCancellation) => publication.ClosePublication(retainCancellation);
    internal void RetainCompletionObserver() => publication.RetainCompletionObserver();
    internal Task BeginCompletion() => publication.BeginCompletion();
    internal TaskCompletionSource? ReleaseCloseObserver() => publication.ReleaseCloseObserver();
    internal void RequirePublicationOpen() => publication.RequirePublicationOpen();

    internal Task PrepareCompletionAsync() => owner.PrepareCompletionAsync(this);

    internal Task<NativeRequestWorkOriginalSettlement> CompleteAsync(Exception? original, Exception? cleanup)
        => owner.CompleteAsync(this, original, cleanup);

    internal async Task FinishAsync(Task start, Task observers)
    {
        await start.ConfigureAwait(false);
        await observers.ConfigureAwait(false);
        Finish();
    }

    internal NativeRequestWorkOriginalSettlement Finish()
    {
        var failures = ImmutableArray.CreateBuilder<Exception>();
        Add(cleanupFailure, failures);
        failures.AddRange(ChildCleanupFailures);
        Add(DisposeCancellation(), failures);
        Add(ReleaseAdmission(), failures);
        var outcome = new NativeRequestWorkOriginalSettlement(originalFailure, failures.ToImmutable());
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
    internal void PrepareDisposal(Exception? original, Exception? cleanup)
        => owner.PrepareDisposal(this, original, cleanup);

    internal void FreezeContext(Exception? original, Exception? cleanup)
    {
        if (contextFrozen)
        {
            return;
        }
        contextFrozen = true;
        originalFailure = original;
        cleanupFailure = cleanup;
    }

    public ValueTask DisposeAsync() => new(owner.CompleteAsync(this, null, null));

    public void Dispose()
    {
        if (owner.TryBeginUnbound(this))
        {
            var outcome = Finish();
            NativeRequestWorkSettlement.Rethrow(outcome.CleanupOrJoinFailure);
        }
    }
}
