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
    private readonly IOptions<NativeAnnExecutionOptions> configuredOptions;
    private readonly NativeAnnGenerationReadLifetime readLifetime;
    private readonly List<NativeAnnGenerationSlot> slots = [];
    private bool closed;
    private readonly NativeAnnWorker worker = new();
    private readonly NativeAnnReadReservations publicReads = new();
    internal NativeAnnMaintenanceMemory MaintenanceMemory { get; }
    private Task? disposal;

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
        configuredOptions = configured;
        readLifetime = new(gate, maximumReaders);
        MaintenanceMemory = new(gate, slots, configured, publicReads);
    }

    internal Task<T> RunAsync<T>(Func<NativeAnnStageStore, T> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return worker.RunAsync(() => operation(stages));
        }
    }

    internal NativeAnnReadReservation ReserveRead(long desiredBytes)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return NativeAnnPublicReadAdmission.Reserve(slots, options, publicReads, desiredBytes, MaintenanceMemory.Bytes);
        }
    }

    internal NativeAnnManifest DescribePublished(NativeAnnOwnedKey key)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return NativeAnnPublicReadAdmission.Describe(slots, key);
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
            NativeAnnSlotRetention.RequireResident(slots, options, checked(seed.PeakBytesUpperBound + publicReads.Bytes));
            return worker.RunAsync(() => BuildAndPublish(request, seed, intent, budget));
        }
    }

    internal NativeAnnManifest BuildAndPublish(AnnMaintenanceRequest request, AnnSeed seed,
        CommitProjectionBatchRequest? intent, AnnWorkBudget budget, long? checkpointAfter = null, long retainedSession = Empty)
    {
        using var transient = MaintenanceMemory.Active ? null : MaintenanceMemory.Reserve();
        var plan = PackedAnnAdmission.Create(seed.Scope.Space, seed.Records, policy.Value, budget);
        _ = MaintenanceMemory.Remaining(checked(retainedSession + NativeAnnPhysicalAdmission.Construction(plan, seed.PeakBytesUpperBound)));
        lock (gate)
        { NativeAnnSlotRetention.RequireResident(slots, options, checked(retainedSession + publicReads.Bytes + NativeAnnPhysicalAdmission.Construction(plan, seed.PeakBytesUpperBound))); }
        var index = PackedAnnIndex.Build(seed.Scope.Space, seed.Records, policy, budget);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            NativeAnnSlotRetention.RequireResident(slots, options, checked(retainedSession + publicReads.Bytes + seed.OwnedBytesUpperBound + index.RetainedBytesUpperBound + index.BuildScratchBytesUpperBound));
            var completed = NativeAnnCompletedPublication.Save(this, root, slots, options, policy, storage, request, seed, index, intent, budget, checkpointAfter);
            MaintenanceMemory.TransferIndex(index.RetainedBytesUpperBound);
            return completed;
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
            NativeAnnSlotRetention.RequireResident(slots, options, checked(index.RetainedBytesUpperBound + publicReads.Bytes));
            var key = new NativeAnnOwnedKey(manifest.Consumer, manifest.IndexGeneration);
            foreach (var slot in slots.Where(item => !item.Retired && NativeAnnSlotRetention.SameKey(item.Manifest, key)))
            { slot.Retire(); }
            slots.Add(new(this, manifest, index));
            MaintenanceMemory.TransferIndex(index.RetainedBytesUpperBound);
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
            return readLifetime.Acquire(slot);
        }
    }

    internal void Release(NativeAnnGenerationSlot slot)
    {
        lock (gate)
        {
            if (!slots.Contains(slot))
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
            readLifetime.Release(slot, root, slots, configuredOptions);
        }
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
            actual = CompleteDisposeAsync(start.Task, readLifetime.OriginalReaders);
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
            MaintenanceMemory.Close();
            slots.Clear();
            ServerFailureObserver.Observe(root.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
