using System.Collections.Immutable;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Owns an original admission, its selective token, and its original async settlement.</summary>
internal sealed class RequestWorkLease : IDisposable, IAsyncDisposable, IRequestWorkBinding
{
    private readonly RequestWorkOwner owner;
    private ResourceLifetimeWorkResultRetention? resultRetention;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource<RequestWorkOriginalSettlement> settlement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RequestWorkLeasePublication publication;
    private bool contextFrozen;
    private Exception? originalFailure;
    private Exception? cleanupFailure;
    public RequestWorkLease OriginalProducer => this;
    internal bool Completing => publication.Completing;
    internal Task? CompletionDriver
    {
        get; set;
    }

    internal RequestWorkLease(RequestWorkOwner owner, Guid id, RequestWorkKind kind, long generation)
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
    internal RequestWorkKind Kind
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
    internal Task<RequestWorkOriginalSettlement> Settlement => settlement.Task;
    internal ImmutableHashSet<ResourceLifetime> Dependencies { get; set; } = [];
    public ResourceLifetimeWorkResultRetention ResultRetention => Volatile.Read(ref resultRetention)
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, RequestWorkLimits.LeaseIdentityMessage);
    internal void AttachResultRetention(ResourceLifetimeWorkResultRetention original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var existing = Interlocked.CompareExchange(ref resultRetention, original, null);
        if (existing is not null && !ReferenceEquals(existing, original))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RequestWorkLimits.LeaseIdentityMessage);
        }
    }
    public void Bind(ResourceLifetimeCoverage coverage) => Bind(coverage.Lifetimes);
    public void Bind(ImmutableArray<ResourceLifetime> actualLifetimes)
    {
        ResultRetention.Reserve(actualLifetimes);
        owner.Bind(this, actualLifetimes);
    }
    internal void AdmitPublication() => owner.AdmitPublication(this);
    internal Task<Exception?> CancelAsync() => RequestWorkSettlement.CancelAsync(cancellation);

    internal Task BeginCompletion() => publication.BeginCompletion();
    internal void RequirePublicationOpen() => publication.RequirePublicationOpen();

    internal Task PrepareCompletionAsync() => owner.PrepareCompletionAsync(this);

    internal Task<RequestWorkOriginalSettlement> CompleteAsync(Exception? original, Exception? cleanup)
        => owner.CompleteAsync(this, original, cleanup);

    internal async Task FinishAsync(Task start)
    {
        await start;
        Finish();
    }

    internal RequestWorkOriginalSettlement Finish()
    {
        var failures = ImmutableArray.CreateBuilder<Exception>();
        Add(cleanupFailure, failures);
        Add(DisposeCancellation(), failures);
        Add(ReleaseAdmission(), failures);
        var outcome = new RequestWorkOriginalSettlement(originalFailure, failures.ToImmutable());
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
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
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
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

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
            RequestWorkSettlement.Rethrow(outcome.CleanupOrJoinFailure);
        }
    }
}
