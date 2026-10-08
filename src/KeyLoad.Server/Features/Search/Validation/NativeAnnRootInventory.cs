using KeyLoad.Storage.IO;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnRootInventory
{
    internal static void Require(string root, NativeAnnRootReceipt receipt, Guid nodeId, Guid incarnation,
        NativeAnnExecutionOptions options)
    {
        if (receipt.Version != NativeAnnProtocol.Version || receipt.Root != root || receipt.NodeId != nodeId
            || receipt.Incarnation != incarnation || receipt.OwnedGenerations is null
            || receipt.OwnedGenerations.Length > options.MaximumOwnedGenerations
            || receipt.Published is null || receipt.Pending is null
            || receipt.Published.Length + receipt.Pending.Length > options.MaximumOwnedGenerations)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leaf in receipt.OwnedGenerations)
        {
            NativeAnnPaths.Generation(root, leaf);
            if (!leaves.Add(leaf))
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        }
        RequirePublished(root, receipt.Published, leaves);
        RequirePublished(root, receipt.Pending, leaves);
        long bytes = NativeAnnFileBounds.Initial;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var leaf = Path.GetFileName(entry);
            if (leaf is NativeAnnProtocol.RootReceipt or NativeAnnProtocol.RootPending or NativeAnnProtocol.RootLock)
            { OfflineRegularFile.RequireRegular(entry); continue; }
            if (!leaves.Contains(leaf))
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
            RequireStage(root, leaf, options, ref bytes);
        }
    }

    private static void RequirePublished(string root, NativeAnnPublished[] records, HashSet<string> leaves)
    {
        var keys = new HashSet<NativeAnnOwnedKey>();
        foreach (var published in records)
        {
            if (published is null || published.Key is null || published.Pointer is null
                || !keys.Add(published.Key) || !leaves.Contains(published.Pointer.GenerationLeaf)
                || published.Pointer.Version != NativeAnnProtocol.Version
                || published.Pointer.ManifestSha256 is null
                || published.Pointer.ManifestSha256.Length != NativeAnnDigest.Bytes)
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
            NativeAnnPaths.RequireClosedGeneration(root, published.Pointer.GenerationLeaf);
        }
    }

    private static void RequireStage(string root, string leaf, NativeAnnExecutionOptions options, ref long bytes)
    {
        var directory = NativeAnnPaths.Generation(root, leaf);
        NativeAnnPaths.RequireDirectory(directory);
        var files = NativeAnnFileBounds.Initial;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            NativeAnnPaths.ExistingFile(root, leaf, Path.GetFileName(entry));
            if (++files > NativeAnnFileBounds.FilesPerGeneration)
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
            var length = new FileInfo(entry).Length;
            if (length > options.MaximumDiskBytes - bytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
            bytes += length;
        }
    }
}
