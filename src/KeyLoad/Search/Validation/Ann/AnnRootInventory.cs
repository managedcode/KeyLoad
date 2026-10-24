using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.Server.Search;

internal static class AnnRootInventory
{
    internal static void Require(string root, AnnRootReceipt receipt, Guid nodeId, Guid incarnation,
        AnnExecutionOptions options)
    {
        if (receipt.Version != AnnProtocol.Version || receipt.Root != root || receipt.NodeId != nodeId
            || receipt.Incarnation != incarnation || receipt.OwnedGenerations is null
            || receipt.OwnedGenerations.Length > options.MaximumOwnedGenerations
            || receipt.Published is null || receipt.Pending is null || receipt.OriginalOperations is null
            || receipt.OriginalOperations.Length > options.MaximumOwnedGenerations
            || receipt.Published.Length + receipt.Pending.Length > options.MaximumOwnedGenerations)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leaf in receipt.OwnedGenerations)
        {
            AnnPaths.Generation(root, leaf);
            if (!leaves.Add(leaf))
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
            }
        }
        RequirePublished(root, receipt.Published, leaves);
        RequirePublished(root, receipt.Pending, leaves);
        var originalKeys = new HashSet<AnnOwnedKey>();
        foreach (var original in receipt.OriginalOperations)
        {
            AnnOriginalResults.Require(original);
            foreach (var request in new[] { original.PendingRequest, original.LastRequest })
            {
                if (request is not null && (request.NodeId != nodeId || request.Placement.Incarnation != incarnation))
                {
                    throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
                }
            }
            if (!originalKeys.Add(original.Key))
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
            }
        }
        long bytes = AnnFileBounds.Initial;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var leaf = Path.GetFileName(entry);
            if (leaf is AnnProtocol.RootReceipt or AnnProtocol.RootPending or AnnProtocol.RootLock)
            {
                OfflineRegularFile.RequireRegular(entry);
                continue;
            }
            if (!leaves.Contains(leaf))
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
            }
            RequireStage(root, leaf, options, ref bytes);
        }
    }

    private static void RequirePublished(string root, AnnPublished[] records, HashSet<string> leaves)
    {
        var keys = new HashSet<AnnOwnedKey>();
        foreach (var published in records)
        {
            if (published is null || published.Key is null || published.Pointer is null
                || !keys.Add(published.Key) || !leaves.Contains(published.Pointer.GenerationLeaf)
                || published.Pointer.Version != AnnProtocol.Version
                || published.Pointer.ManifestSha256 is null
                || published.Pointer.ManifestSha256.Length != AnnDigest.Bytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
            }
            AnnPaths.RequireClosedGeneration(root, published.Pointer.GenerationLeaf);
        }
    }

    private static void RequireStage(string root, string leaf, AnnExecutionOptions options, ref long bytes)
    {
        var directory = AnnPaths.Generation(root, leaf);
        AnnPaths.RequireDirectory(directory);
        var files = AnnFileBounds.Initial;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            AnnPaths.ExistingFile(root, leaf, Path.GetFileName(entry));
            if (++files > AnnFileBounds.FilesPerGeneration)
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
            }
            var length = new FileInfo(entry).Length;
            if (length > options.MaximumDiskBytes - bytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
            }
            bytes += length;
        }
    }
}
