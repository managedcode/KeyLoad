using Microsoft.Extensions.Options;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

/// <summary>Verifies exact owned publication without unpublishing, promoting or selecting a directory.</summary>
internal static class NativeAnnProjectionAuthority
{
    internal static NativeAnnManifest ReadPublished(NativeAnnRootLease root, AnnMaintenanceRequest request,
        AnnSeed current, IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage,
        NativeAnnExecutionOptions options, AnnSeedOptions seeds, long maximumPeak,
        AnnWorkBudget indexBudget, ReadExecutionBudget readBudget)
    {
        indexBudget.Check();
        readBudget.Check();
        NativeAnnRootInventory.Require(root.Root, root.Receipt, current.Cut.NodeId,
            current.Cut.Incarnation, options);
        var key = new NativeAnnOwnedKey(request.Consumer, request.IndexGeneration);
        if (root.Receipt.Pending.Any(item => item.Key == key))
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
        }
        var selected = root.Receipt.Published.SingleOrDefault(item => item.Key == key)
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
        var loaded = NativeAnnLoadedSources.Load(root.Root, selected.Pointer, request, current, policy,
            storage, options, seeds, maximumPeak, indexBudget, readBudget);
        indexBudget.Check();
        readBudget.Check();
        return loaded.Manifest;
    }
}
