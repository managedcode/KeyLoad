using KeyLoad.Clustering;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnGenerationOwner : IAsyncDisposable
{
    private const int Empty = 0;
    private readonly Lock gate = new();
    private readonly AnnRootLease root;
    private readonly AnnStageStore stages;
    private readonly AnnExecutionOptions options;
    private readonly IOptions<PackedAnnOptions> policy;
    private readonly IOptions<PackedAnnStorageOptions> storage;
    private readonly IOptions<AnnExecutionOptions> configuredOptions;
    private readonly AnnGenerationReadLifetime readLifetime;
    private readonly List<AnnGenerationSlot> slots = [];
    private bool closed;
    private readonly AnnWorker worker = new();
    private readonly AnnReadReservations publicReads = new();
    internal AnnMaintenanceMemory MaintenanceMemory
    {
        get;
    }
    private Task? disposal;

    internal AnnGenerationOwner(AnnRootLease root, IOptions<AnnExecutionOptions> configured,
        IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage, int maximumReaders)
    {
        this.root = root;
        stages = new(root, configured, policy, storage);
        options = configured.Value;
        options.Validate();
        policy.Value.Validate();
        storage.Value.Validate();
        if (maximumReaders <= Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, AnnProtocol.Bound);
        }
        this.policy = policy;
        this.storage = storage;
        configuredOptions = configured;
        readLifetime = new(gate, maximumReaders);
        MaintenanceMemory = new(gate, slots, configured, publicReads);
    }

    internal Task<T> RunAsync<T>(Func<AnnStageStore, T> operation)
        => AnnOwnerAdmission.RunAsync(this, gate, ref closed, worker, stages, operation);

    internal AnnReadReservation ReserveRead(long desiredBytes)
        => AnnOwnerAdmission.ReserveRead(this, gate, ref closed, slots, options, publicReads, MaintenanceMemory, desiredBytes);

    internal AnnManifest DescribePublished(AnnOwnedKey key)
        => AnnOwnerAdmission.DescribePublished(this, gate, ref closed, slots, key);

    internal Task<AnnManifest> BuildAndPublishAsync(AnnMaintenanceRequest request, AnnSeed seed,
        CommitProjectionBatchRequest? intent, AnnWorkBudget budget)
    {
        AnnCanonicalSource.RequireRequest(request, seed);
        budget.Check();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            AnnSlotRetention.RequireResident(slots, options, checked(seed.PeakBytesUpperBound + publicReads.Bytes));
            return Task.FromResult(worker.Run(() => BuildAndPublish(request, seed, intent, budget)));
        }
    }

    internal AnnManifest BuildAndPublish(AnnMaintenanceRequest request, AnnSeed seed,
        CommitProjectionBatchRequest? intent, AnnWorkBudget budget, long? checkpointAfter = null, long retainedSession = Empty)
    {
        using var transient = MaintenanceMemory.Active ? null : MaintenanceMemory.Reserve();
        var plan = PackedAnnAdmission.Create(seed.Scope.Space, seed.Records, policy.Value, budget);
        _ = MaintenanceMemory.Remaining(checked(retainedSession + AnnPhysicalAdmission.Construction(plan, seed.PeakBytesUpperBound)));
        lock (gate)
        {
            AnnSlotRetention.RequireResident(slots, options, checked(retainedSession + publicReads.Bytes + AnnPhysicalAdmission.Construction(plan, seed.PeakBytesUpperBound)));
        }
        var index = PackedAnnIndex.Build(seed.Scope.Space, seed.Records, policy, budget);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            AnnSlotRetention.RequireResident(slots, options, checked(retainedSession + publicReads.Bytes + seed.OwnedBytesUpperBound + index.RetainedBytesUpperBound + index.BuildScratchBytesUpperBound));
            var completed = AnnCompletedPublication.Save(this, root, slots, options, policy, storage, request, seed, index, intent, budget, checkpointAfter);
            MaintenanceMemory.TransferIndex(index.RetainedBytesUpperBound);
            return completed;
        }
    }

    internal AnnManifest AdmitLoaded(PackedAnnIndex index, AnnManifest manifest, AnnSeed current)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (manifest.IsPending || current.CorpusSha256 != manifest.Source.CorpusSha256
                || current.ProjectionCheckpoint != manifest.Source.ThroughSequence)
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
            }
            if (slots.Any(item => !item.Retired && item.Manifest.GenerationLeaf == manifest.GenerationLeaf))
            {
                return manifest;
            }
            AnnSlotRetention.RequireResident(slots, options, checked(index.RetainedBytesUpperBound + publicReads.Bytes));
            var key = new AnnOwnedKey(manifest.Consumer, manifest.IndexGeneration);
            foreach (var slot in slots.Where(item => !item.Retired && AnnSlotRetention.SameKey(item.Manifest, key)))
            {
                slot.Retire();
            }
            slots.Add(new(this, manifest, index));
            MaintenanceMemory.TransferIndex(index.RetainedBytesUpperBound);
            AnnSlotRetention.RetireUnpinned(root, slots, options);
            return manifest;
        }
    }

    internal void ReleaseGeneration(AnnOwnedKey key)
        => AnnOwnerAdmission.ReleaseGeneration(this, gate, ref closed, stages, slots, options, root, key);

    internal AnnIndexLease Acquire(AnnMaintenanceRequest request, AnnSeed current)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
            var slot = slots.SingleOrDefault(item => !item.Retired && AnnSlotRetention.SameKey(item.Manifest, key))
                ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
            if (!root.Receipt.Published.Any(item => item.Key == key && item.Pointer.GenerationLeaf == slot.Manifest.GenerationLeaf)
                || root.Receipt.Pending.Any(item => item.Key == key))
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
            }
            AnnCanonicalSource.RequireRestoreIdentity(request, current, slot.Manifest);
            if (current.DependencySha256 != slot.Manifest.Source.DependencySha256)
            {
                root.Unpublish(key, options);
                slot.Retire();
                AnnSlotRetention.RetireUnpinned(root, slots, options);
                throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
            }
            AnnCanonicalSource.RequireRestore(request, current, slot.Manifest);
            if (current.ProjectionCheckpoint != slot.Manifest.Source.ThroughSequence
                || slot.Manifest.Source.ThroughSequence != current.Cut.OutboxTail
                || slot.Manifest.Source.CorpusSha256 != current.CorpusSha256)
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
            }
            return readLifetime.Acquire(slot);
        }
    }

    internal void Release(AnnGenerationSlot slot)
    {
        lock (gate)
        {
            if (!slots.Contains(slot))
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
            }
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
            {
                return new(disposal);
            }
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
        await start;
        var failures = new List<Exception>();
        try
        {
            await worker.DisposeAsync();
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        await AnnGenerationShutdown.CompleteAsync(originalReaders, gate, MaintenanceMemory, slots, root, failures);
    }
}
