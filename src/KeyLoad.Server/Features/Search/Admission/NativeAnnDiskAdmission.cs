using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnDiskAdmission
{
    private const int ReceiptReservations = 3;
    private const long MinimumFileBytes = 1_024;
    private const int Empty = 0;

    internal static IOptions<PackedAnnStorageOptions> ForStage(string root, NativeAnnRootReceipt receipt,
        IOptions<PackedAnnStorageOptions> storage, NativeAnnExecutionOptions options)
    {
        NativeAnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        long bytes = Empty;
        foreach (var leaf in receipt.OwnedGenerations)
        {
            var directory = NativeAnnPaths.Generation(root, leaf);
            if (!Directory.Exists(directory))
            { continue; }
            foreach (var path in Directory.EnumerateFiles(directory))
            { bytes = checked(bytes + new FileInfo(path).Length); }
        }
        var overhead = checked((long)ReceiptReservations * options.MaximumManifestBytes
            + new FileInfo(Path.Combine(root, NativeAnnProtocol.RootLock)).Length);
        var remaining = options.MaximumDiskBytes - bytes - overhead;
        if (remaining < MinimumFileBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        return NativeAnnStorageOptionsFactory.ForFile(storage, remaining);
    }
}
