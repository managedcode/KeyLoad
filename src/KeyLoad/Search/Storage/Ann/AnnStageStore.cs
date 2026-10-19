using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnStageStore(AnnRootLease root, IOptions<AnnExecutionOptions> configured,
    IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage)
{
    private const int Empty = 0;
    private readonly AnnExecutionOptions options = configured.Value;

    internal global::KeyLoad.Clustering.AnnProjectionEnrollment? ReadEnrollment(AnnMaintenanceRequest request)
    {
        var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
        var entry = root.Receipt.OriginalOperations.SingleOrDefault(item => item.Key == key);
        if (entry is null)
        {
            return null;
        }
        AnnOriginalResults.Require(entry);
        if (!AnnOriginalResults.SameRequest(entry.InitialRequest, request))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        if (entry.Released || !entry.InitialSettled || entry.PendingRequest is not null)
        {
            return null;
        }
        return new(entry.InitialRequest,
            entry.LastRequest ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt),
            entry.LastResult ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt));
    }

    internal AnnMaintenanceResult? AdmitOriginal(PrincipalRecord principal, AnnMaintenanceRequest request,
        ProjectionConsumerInfo configured, AnnSeed current)
        => root.AdmitOriginal(principal, request, configured, current, options);

    internal CommitProjectionBatchRequest? OriginalSettlementIntent(AnnMaintenanceRequest request)
    {
        var entry = root.Receipt.OriginalOperations.SingleOrDefault(value => value.Key == new AnnOwnedKey(request.Consumer, request.IndexGeneration));
        return entry is not null && AnnOriginalResults.SameRequest(entry.InitialRequest, request)
            ? entry.InitialSettlementIntent : null;
    }

    internal void StartOriginal(AnnMaintenanceRequest request) => root.StartOriginal(request, options);

    internal void SettleOriginal(PrincipalRecord principal, AnnMaintenanceRequest request,
        AnnMaintenanceResult result, AnnSeed current, CommitProjectionBatchRequest? settlementIntent)
        => root.SettleOriginal(principal, request, result, current, options, settlementIntent);

    internal AnnManifest Describe(AnnMaintenanceRequest request, AnnSeed current)
    {
        var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
        var selected = root.Receipt.Pending.SingleOrDefault(item => item.Key == key)
            ?? root.Receipt.Published.SingleOrDefault(item => item.Key == key)
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        var observed = AnnReceiptFiles.Read<AnnManifest>(
            AnnPaths.ExistingFile(root.Root, selected.Pointer.GenerationLeaf, AnnProtocol.ManifestFile), options);
        AnnDigest.Require(selected.Pointer.ManifestSha256, observed.Digest);
        var manifest = observed.Value;
        AnnCanonicalSource.RequireRestoreIdentity(request, current, manifest);
        if (current.DependencySha256 != manifest.Source.DependencySha256)
        {
            root.Unpublish(key, options);
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
        AnnCanonicalSource.RequireRestore(request, current, manifest);
        RequireProgress(manifest, current);
        return manifest;
    }

    internal AnnManifest ReadPublished(AnnMaintenanceRequest request, AnnSeed current,
        AnnSeedOptions seeds, long maximumPeak, AnnWorkBudget budget, ReadExecutionBudget readBudget)
        => AnnProjectionAuthority.ReadPublished(root, request, current, policy, storage,
            options, seeds, maximumPeak, budget, readBudget);

    internal AnnReplaySource LoadPending(AnnMaintenanceRequest request, AnnSeed current,
        AnnSeedOptions seeds, AnnWorkBudget budget, ReadExecutionBudget readBudget)
    {
        var manifest = Describe(request, current);
        if (!manifest.IsPending)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        return AnnPendingFiles.Load(root.Root, manifest, request, current, storage, options, seeds, budget, readBudget);
    }

    internal AnnLoadedSource LoadCompleted(AnnMaintenanceRequest request, AnnSeed current,
        AnnSeedOptions seeds, long maximumPeak, AnnWorkBudget budget, ReadExecutionBudget readBudget)
    {
        var manifest = Describe(request, current);
        if (manifest.IsPending)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
        var pointer = root.Receipt.Published.Single(item => item.Key == key).Pointer;
        return AnnLoadedSources.Load(root.Root, pointer, request, current, policy, storage,
            options, seeds, maximumPeak, budget, readBudget);
    }

    internal AnnManifest SavePending(AnnPendingReplay pending, AnnWorkBudget budget)
    {
        var key = new AnnOwnedKey(pending.AdmittedUpper.Consumer, pending.AdmittedUpper.IndexGeneration);
        var previous = root.Receipt.Pending.SingleOrDefault(item => item.Key == key);
        var leaf = AnnProtocol.GenerationPrefix + Guid.NewGuid().ToString(AnnProtocol.GuidFormat);
        var bounded = AnnDiskAdmission.ForStage(root.Root, root.Receipt, storage, options);
        root.Enroll(leaf, options);
        var staged = false;
        try
        {
            var saved = AnnPendingFiles.Save(root.Root, root.Receipt, leaf, pending, bounded, options, budget);
            budget.Check();
            root.Stage(key, saved.Pointer, options);
            staged = true;
            if (previous is not null)
            {
                root.Remove(previous.Pointer.GenerationLeaf, options);
            }
            return saved.Manifest;
        }
        catch (Exception primary)
        {
            if (!staged)
            {
                try
                {
                    root.Remove(leaf, options);
                }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            }
            throw;
        }
    }

    internal void Release(AnnOwnedKey key)
    {
        var pending = root.Receipt.Pending.SingleOrDefault(item => item.Key == key);
        root.Unpublish(key, options, releaseOriginal: true);
        if (pending is not null)
        {
            root.Remove(pending.Pointer.GenerationLeaf, options);
        }
    }

    private static void RequireProgress(AnnManifest manifest, AnnSeed current)
    {
        var through = manifest.IsPending ? manifest.ReplayThrough : manifest.Source.ThroughSequence;
        if (manifest.Version != AnnProtocol.Version || manifest.CheckpointIntent is null
            || manifest.CheckpointIntent.Consumer != manifest.Consumer || !manifest.CheckpointIntent.Effects.IsEmpty
            || manifest.ReplayAfter < Empty || through < manifest.ReplayAfter || through > manifest.Source.ThroughSequence
            || current.ProjectionCheckpoint != manifest.ReplayAfter && current.ProjectionCheckpoint != through)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
        if (manifest.IsPending && (current.Cut.OutboxTail != manifest.Source.ThroughSequence
            || current.CorpusSha256 != manifest.Source.CorpusSha256))
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
    }
}
