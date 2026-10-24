using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

/// <summary>Verifies exact owned publication without unpublishing, promoting or selecting a directory.</summary>
internal static class AnnProjectionAuthority
{
    internal static AnnManifest ReadPublished(AnnRootLease root, AnnMaintenanceRequest request,
        AnnSeed current, IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage,
        AnnExecutionOptions options, AnnSeedOptions seeds, long maximumPeak,
        AnnWorkBudget indexBudget, ReadExecutionBudget readBudget)
    {
        indexBudget.Check();
        readBudget.Check();
        AnnRootInventory.Require(root.Root, root.Receipt, current.Cut.NodeId,
            current.Cut.Incarnation, options);
        var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
        if (root.Receipt.Pending.Any(item => item.Key == key))
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
        var selected = root.Receipt.Published.SingleOrDefault(item => item.Key == key)
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        var loaded = AnnLoadedSources.Load(root.Root, selected.Pointer, request, current, policy,
            storage, options, seeds, maximumPeak, indexBudget, readBudget);
        indexBudget.Check();
        readBudget.Check();
        return loaded.Manifest;
    }
}
