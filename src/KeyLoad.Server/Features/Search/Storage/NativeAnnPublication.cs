namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnPublication
{
    internal static NativeAnnRootReceipt Publish(string root, NativeAnnRootReceipt receipt,
        NativeAnnOwnedKey key, NativeAnnPointer pointer, NativeAnnExecutionOptions options)
    {
        NativeAnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        NativeAnnPaths.RequireClosedGeneration(root, pointer.GenerationLeaf);
        if (!receipt.OwnedGenerations.Contains(pointer.GenerationLeaf, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var observed = NativeAnnReceiptFiles.Read<NativeAnnManifest>(
            NativeAnnPaths.ExistingFile(root, pointer.GenerationLeaf, NativeAnnProtocol.ManifestFile), options);
        NativeAnnDigest.Require(pointer.ManifestSha256, observed.Digest);
        if (observed.Value.IsPending || observed.Value.Consumer != key.Consumer || observed.Value.IndexGeneration != key.IndexGeneration)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.InvalidSource); }
        var prior = receipt.Published.Where(item => item.Key != key).ToArray();
        if (prior.Length == options.MaximumOwnedGenerations)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        var next = receipt with
        {
            Published = [.. prior, new(key, pointer)],
            Pending = receipt.Pending.Where(item => item.Key != key).ToArray()
        };
        NativeAnnRootInventory.Require(root, next, next.NodeId, next.Incarnation, options);
        NativeAnnRootReceipts.Replace(root, next, options);
        return next;
    }

    internal static NativeAnnRootReceipt Stage(string root, NativeAnnRootReceipt receipt,
        NativeAnnOwnedKey key, NativeAnnPointer pointer, NativeAnnExecutionOptions options)
    {
        NativeAnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        NativeAnnPaths.RequireClosedGeneration(root, pointer.GenerationLeaf);
        if (!receipt.OwnedGenerations.Contains(pointer.GenerationLeaf, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var observed = NativeAnnReceiptFiles.Read<NativeAnnManifest>(
            NativeAnnPaths.ExistingFile(root, pointer.GenerationLeaf, NativeAnnProtocol.ManifestFile), options);
        NativeAnnDigest.Require(pointer.ManifestSha256, observed.Digest);
        if (!observed.Value.IsPending || observed.Value.Consumer != key.Consumer
            || observed.Value.IndexGeneration != key.IndexGeneration)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        var next = receipt with { Pending = [.. receipt.Pending.Where(item => item.Key != key), new(key, pointer)] };
        NativeAnnRootInventory.Require(root, next, next.NodeId, next.Incarnation, options);
        NativeAnnRootReceipts.Replace(root, next, options);
        return next;
    }

    internal static NativeAnnRootReceipt Unpublish(string root, NativeAnnRootReceipt receipt,
        NativeAnnOwnedKey key, NativeAnnExecutionOptions options)
    {
        NativeAnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        var next = receipt with
        {
            Published = receipt.Published.Where(item => item.Key != key).ToArray(),
            Pending = receipt.Pending.Where(item => item.Key != key).ToArray()
        };
        NativeAnnRootReceipts.Replace(root, next, options);
        return next;
    }

    internal static NativeAnnRootReceipt Remove(string root, NativeAnnRootReceipt receipt,
        string leaf, NativeAnnExecutionOptions options)
    {
        NativeAnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        if (!receipt.OwnedGenerations.Contains(leaf, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var retired = receipt with
        {
            Published = receipt.Published.Where(item => item.Pointer.GenerationLeaf != leaf).ToArray(),
            Pending = receipt.Pending.Where(item => item.Pointer.GenerationLeaf != leaf).ToArray()
        };
        NativeAnnRootReceipts.Replace(root, retired, options);
        var directory = NativeAnnPaths.Generation(root, leaf);
        if (Directory.Exists(directory))
        { Directory.Delete(directory, recursive: true); }
        var next = retired with
        { OwnedGenerations = retired.OwnedGenerations.Where(item => item != leaf).ToArray() };
        NativeAnnRootReceipts.Replace(root, next, options);
        return next;
    }
}
