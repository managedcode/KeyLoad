using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnStageStore(NativeAnnRootLease root, IOptions<NativeAnnExecutionOptions> configured,
    IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage)
{
    private const int Empty = 0;
    private readonly NativeAnnExecutionOptions options = configured.Value;

    internal NativeAnnManifest Describe(AnnMaintenanceRequest request, AnnSeed current)
    {
        var key = new NativeAnnOwnedKey(request.Consumer, request.IndexGeneration);
        var selected = root.Receipt.Pending.SingleOrDefault(item => item.Key == key)
            ?? root.Receipt.Published.SingleOrDefault(item => item.Key == key)
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
        var observed = NativeAnnReceiptFiles.Read<NativeAnnManifest>(
            NativeAnnPaths.ExistingFile(root.Root, selected.Pointer.GenerationLeaf, NativeAnnProtocol.ManifestFile), options);
        NativeAnnDigest.Require(selected.Pointer.ManifestSha256, observed.Digest);
        var manifest = observed.Value;
        NativeAnnCanonicalSource.RequireRestoreIdentity(request, current, manifest);
        if (current.DependencySha256 != manifest.Source.DependencySha256)
        {
            root.Unpublish(key, options);
            throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
        }
        NativeAnnCanonicalSource.RequireRestore(request, current, manifest);
        RequireProgress(manifest, current);
        return manifest;
    }

    internal NativeAnnReplaySource LoadPending(AnnMaintenanceRequest request, AnnSeed current,
        AnnSeedOptions seeds, AnnWorkBudget budget, ReadExecutionBudget readBudget)
    {
        var manifest = Describe(request, current);
        if (!manifest.IsPending)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        return NativeAnnPendingFiles.Load(root.Root, manifest, request, current, storage, options, seeds, budget, readBudget);
    }

    internal NativeAnnLoadedSource LoadCompleted(AnnMaintenanceRequest request, AnnSeed current,
        AnnSeedOptions seeds, long maximumPeak, AnnWorkBudget budget, ReadExecutionBudget readBudget)
    {
        var manifest = Describe(request, current);
        if (manifest.IsPending)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        var key = new NativeAnnOwnedKey(request.Consumer, request.IndexGeneration);
        var pointer = root.Receipt.Published.Single(item => item.Key == key).Pointer;
        return NativeAnnLoadedSources.Load(root.Root, pointer, request, current, policy, storage,
            options, seeds, maximumPeak, budget, readBudget);
    }

    internal NativeAnnManifest SavePending(NativeAnnPendingReplay pending, AnnWorkBudget budget)
    {
        var key = new NativeAnnOwnedKey(pending.AdmittedUpper.Consumer, pending.AdmittedUpper.IndexGeneration);
        var previous = root.Receipt.Pending.SingleOrDefault(item => item.Key == key);
        var leaf = NativeAnnProtocol.GenerationPrefix + Guid.NewGuid().ToString(NativeAnnProtocol.GuidFormat);
        var bounded = NativeAnnDiskAdmission.ForStage(root.Root, root.Receipt, storage, options);
        root.Enroll(leaf, options);
        var staged = false;
        try
        {
            var saved = NativeAnnPendingFiles.Save(root.Root, root.Receipt, leaf, pending, bounded, options, budget);
            budget.Check();
            root.Stage(key, saved.Pointer, options);
            staged = true;
            if (previous is not null)
            { root.Remove(previous.Pointer.GenerationLeaf, options); }
            return saved.Manifest;
        }
        catch (Exception primary)
        {
            if (!staged)
            {
                try
                { root.Remove(leaf, options); }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            }
            throw;
        }
    }

    internal void Release(NativeAnnOwnedKey key)
    {
        var pending = root.Receipt.Pending.SingleOrDefault(item => item.Key == key);
        root.Unpublish(key, options);
        if (pending is not null)
        { root.Remove(pending.Pointer.GenerationLeaf, options); }
    }

    private static void RequireProgress(NativeAnnManifest manifest, AnnSeed current)
    {
        var through = manifest.IsPending ? manifest.ReplayThrough : manifest.Source.ThroughSequence;
        if (manifest.Version != NativeAnnProtocol.Version || manifest.CheckpointIntent is null
            || manifest.CheckpointIntent.Consumer != manifest.Consumer || !manifest.CheckpointIntent.Effects.IsEmpty
            || manifest.ReplayAfter < Empty || through < manifest.ReplayAfter || through > manifest.Source.ThroughSequence
            || current.ProjectionCheckpoint != manifest.ReplayAfter && current.ProjectionCheckpoint != through)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
        if (manifest.IsPending && (current.Cut.OutboxTail != manifest.Source.ThroughSequence
            || current.CorpusSha256 != manifest.Source.CorpusSha256))
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
    }
}
