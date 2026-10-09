using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnPublicReadLease : IAnnProjectionLease
{
    private readonly Lock gate = new();
    private readonly NativeAnnIndexLease current;
    private readonly NativeAnnReadReservation memory;
    private readonly AnnSeed source;
    private readonly IOptions<AnnSeedOptions> callerOptions;
    private readonly AnnSeedWork scopeWork;
    private bool disposed;

    internal NativeAnnPublicReadLease(DatabaseEngine database, IKeyValueView view,
        NativeAnnGenerationOwner owner, AnnProjectionSelection request,
        ServerRuntimeOptions configured, ReadExecutionBudget budget)
    {
        try
        {
            budget.Check();
            var manifest = owner.DescribePublished(new(request.Consumer, request.IndexGeneration));
            NativeAnnPublicAcquisition.RequireScope(request, manifest);
            memory = owner.ReserveRead(NativeAnnPublicFrameAdmission.Desired(configured, budget));
            callerOptions = NativeAnnPublicFrameAdmission.CaptureOptions(configured, budget, memory.Bytes);
            var pinned = NativeAnnPublicAcquisition.Request(manifest);
            scopeWork = new AnnSeedWork(budget, callerOptions.Value.MaxWorkUnits);
            source = AnnSeedCollector.CapturePinnedView(database, view, manifest.PrincipalId,
                pinned, callerOptions, budget, scopeWork);
            current = owner.Acquire(pinned, source);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { Dispose(); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public long IndexedAppliedPosition
    {
        get { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); return current.Manifest.Source.AppliedPosition; } }
    }
    public long IndexGeneration
    {
        get { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); return current.Manifest.IndexGeneration; } }
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
            { return; }
            disposed = true;
            var failures = new List<Exception>();
            try
            { current?.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            try
            { memory?.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
