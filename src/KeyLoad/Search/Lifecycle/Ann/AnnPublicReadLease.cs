using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnPublicReadLease : IAnnProjectionLease
{
    private readonly Lock gate = new();
    private readonly AnnIndexLease current;
    private readonly AnnReadReservation memory;
    private readonly AnnSeed source;
    private readonly IOptions<AnnSeedOptions> callerOptions;
    private readonly AnnSeedWork scopeWork;
    private bool disposed;

    internal AnnPublicReadLease(DatabaseEngine database, IKeyValueView view,
        AnnGenerationOwner owner, AnnProjectionSelection request,
        ServerRuntimeOptions configured, ReadExecutionBudget budget)
    {
        try
        {
            budget.Check();
            var manifest = owner.DescribePublished(new(request.Consumer, request.IndexGeneration));
            AnnPublicAcquisition.RequireScope(request, manifest);
            memory = owner.ReserveRead(AnnPublicFrameAdmission.Desired(configured, budget));
            callerOptions = AnnPublicFrameAdmission.CaptureOptions(configured, budget, memory.Bytes);
            var pinned = AnnPublicAcquisition.Request(database, view, manifest, budget);
            scopeWork = new AnnSeedWork(budget, callerOptions.Value.MaxWorkUnits);
            source = AnnSeedCollector.CapturePinnedView(database, view, manifest.PrincipalId,
                pinned, callerOptions, budget, scopeWork);
            current = owner.Acquire(pinned, source);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            {
                Dispose();
            }
            catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public long IndexedAppliedPosition
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return current.Manifest.Source.AppliedPosition;
            }
        }
    }
    public long IndexGeneration
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return current.Manifest.IndexGeneration;
            }
        }
    }
    public ImmutableArray<VectorRecord> Records => source.Records;
    public IOptions<AnnSeedOptions> CallerSeedOptions => callerOptions;
    public AnnSeedWork ScopeWork => scopeWork;

    public AnnSearchResult Search(ReadOnlyMemory<float> query, int limit,
        ReadOnlyMemory<ulong> eligible, AnnWorkBudget budget)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return current.Index.Search(query, limit, eligible, budget);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            var failures = new List<Exception>();
            try
            {
                current?.Dispose();
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            try
            {
                memory?.Dispose();
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
