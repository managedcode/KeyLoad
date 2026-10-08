using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnGenerationOwner : IAsyncDisposable
{
    private const int Empty = 0;
    private readonly Lock gate = new();
    private readonly NativeAnnRootLease root;
    private readonly NativeAnnStageStore stages;
    private readonly NativeAnnExecutionOptions options;
    private readonly IOptions<PackedAnnOptions> policy;
    private readonly IOptions<PackedAnnStorageOptions> storage;
    private readonly int maximumReaders;
    private readonly List<NativeAnnGenerationSlot> slots = [];
    private bool closed;
    private readonly NativeAnnWorker worker = new();
    private Task? disposal;
    private TaskCompletionSource? noReaders;
    private int readers;
    private Exception? readerFailure;

    internal NativeAnnGenerationOwner(NativeAnnRootLease root, IOptions<NativeAnnExecutionOptions> configured,
        IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage, int maximumReaders)
    {
        this.root = root;
        stages = new(root, configured, policy, storage);
        options = configured.Value;
        options.Validate();
        policy.Value.Validate();
        storage.Value.Validate();
        if (maximumReaders <= Empty)
        { throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.Bound); }
        this.policy = policy;
        this.storage = storage;
        this.maximumReaders = maximumReaders;
    }

    internal Task<T> RunAsync<T>(Func<NativeAnnStageStore, T> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return worker.RunAsync(() => operation(stages));
        }
    }

    internal long RemainingResident(long incoming)
    {
        lock (gate)
        {
            NativeAnnSlotRetention.RequireResident(slots, options, incoming);
            return checked(options.MaximumResidentBytes - incoming - slots.Sum(item => item.Index.RetainedBytesUpperBound));
        }
    }

    internal Task<NativeAnnManifest> BuildAndPublishAsync(AnnMaintenanceRequest request, AnnSeed seed,
        CommitProjectionBatchRequest? intent, AnnWorkBudget budget)
    {
        NativeAnnCanonicalSource.RequireRequest(request, seed);
        budget.Check();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            NativeAnnSlotRetention.RequireResident(slots, options, seed.PeakBytesUpperBound);
            return worker.RunAsync(() => BuildAndPublish(request, seed, intent, budget));
        }
    }

    internal NativeAnnManifest BuildAndPublish(AnnMaintenanceRequest request, AnnSeed seed,
        CommitProjectionBatchRequest? intent, AnnWorkBudget budget, long? checkpointAfter = null, long retainedSession = Empty)
    {
        var plan = PackedAnnAdmission.Create(seed.Scope.Space, seed.Records, policy.Value, budget);
        lock (gate)
        { NativeAnnSlotRetention.RequireResident(slots, options, checked(retainedSession + NativeAnnPhysicalAdmission.Construction(plan, seed.PeakBytesUpperBound))); }
        var index = PackedAnnIndex.Build(seed.Scope.Space, seed.Records, policy, budget);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            NativeAnnSlotRetention.RequireResident(slots, options, checked(retainedSession + seed.OwnedBytesUpperBound + index.RetainedBytesUpperBound + index.BuildScratchBytesUpperBound));
            return NativeAnnCompletedPublication.Save(this, root, slots, options, policy, storage, request, seed, index, intent, budget, checkpointAfter);
        }
    }

    internal NativeAnnManifest AdmitLoaded(PackedAnnIndex index, NativeAnnManifest manifest, AnnSeed current)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (manifest.IsPending || current.CorpusSha256 != manifest.Source.CorpusSha256
                || current.ProjectionCheckpoint != manifest.Source.ThroughSequence)
            { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
            if (slots.Any(item => !item.Retired && item.Manifest.GenerationLeaf == manifest.GenerationLeaf))
            { return manifest; }
            NativeAnnSlotRetention.RequireResident(slots, options, index.RetainedBytesUpperBound);
            var key = new NativeAnnOwnedKey(manifest.Consumer, manifest.IndexGeneration);
            foreach (var slot in slots.Where(item => !item.Retired && NativeAnnSlotRetention.SameKey(item.Manifest, key)))
            { slot.Retire(); }
            slots.Add(new(this, manifest, index));
            NativeAnnSlotRetention.RetireUnpinned(root, slots, options);
            return manifest;
        }
    }

    internal void ReleaseGeneration(NativeAnnOwnedKey key)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            stages.Release(key);
            foreach (var slot in slots.Where(item => NativeAnnSlotRetention.SameKey(item.Manifest, key)))
            { slot.Retire(); }
            NativeAnnSlotRetention.RetireUnpinned(root, slots, options);
        }
    }

    internal NativeAnnIndexLease Acquire(AnnMaintenanceRequest request, AnnSeed current)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var key = new NativeAnnOwnedKey(request.Consumer, request.IndexGeneration);
            var slot = slots.SingleOrDefault(item => !item.Retired && NativeAnnSlotRetention.SameKey(item.Manifest, key))
                ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
            if (!root.Receipt.Published.Any(item => item.Key == key && item.Pointer.GenerationLeaf == slot.Manifest.GenerationLeaf)
                || root.Receipt.Pending.Any(item => item.Key == key))
            { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
            NativeAnnCanonicalSource.RequireRestoreIdentity(request, current, slot.Manifest);
            if (current.DependencySha256 != slot.Manifest.Source.DependencySha256)
            {
                root.Unpublish(key, options);
                slot.Retire();
                NativeAnnSlotRetention.RetireUnpinned(root, slots, options);
                throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
            }
            NativeAnnCanonicalSource.RequireRestore(request, current, slot.Manifest);
            if (current.ProjectionCheckpoint != slot.Manifest.Source.ThroughSequence
                || slot.Manifest.Source.ThroughSequence != current.Cut.OutboxTail
                || slot.Manifest.Source.CorpusSha256 != current.CorpusSha256)
            { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
            if (readers == maximumReaders)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound); }
            var pendingZero = readers == Empty ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : noReaders;
            var lease = slot.Acquire(maximumReaders);
            noReaders = pendingZero;
            readers++;
            return lease;
        }
    }

    internal void Release(NativeAnnGenerationSlot slot)
    {
        lock (gate)
        {
            if (!slots.Contains(slot))
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
            slot.ReleaseUnderOwnerGate();
            readers--;
            try
            { NativeAnnSlotRetention.RetireUnpinned(root, slots, options); }
            catch (Exception error)
            {
                readerFailure = readerFailure is null ? error : new AggregateException(readerFailure, error);
                throw;
            }
            finally { if (readers == Empty) { SignalReaders(); } }
        }
    }

    private void SignalReaders()
    {
        if (readerFailure is null)
        { noReaders?.TrySetResult(); }
        else
        { noReaders?.TrySetException(readerFailure); }
        noReaders = null;
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource start;
        Task actual;
        lock (gate)
        {
            if (disposal is not null)
            { return new(disposal); }
            closed = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = CompleteDisposeAsync(start.Task, noReaders?.Task
                ?? (readerFailure is null ? Task.CompletedTask : Task.FromException(readerFailure)));
            disposal = actual;
        }
        start.TrySetResult();
        return new(actual);
    }

    private async Task CompleteDisposeAsync(Task start, Task originalReaders)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        try
        { await worker.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures).ConfigureAwait(false);
        lock (gate)
        {
            slots.Clear();
            ServerFailureObserver.Observe(root.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
