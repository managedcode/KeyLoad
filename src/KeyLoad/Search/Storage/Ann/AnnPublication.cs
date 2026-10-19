namespace KeyLoad.Server.Features.Search;

internal static class AnnPublication
{
    internal static AnnRootReceipt Publish(string root, AnnRootReceipt receipt,
        AnnOwnedKey key, AnnPointer pointer, AnnExecutionOptions options)
    {
        AnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        AnnPaths.RequireClosedGeneration(root, pointer.GenerationLeaf);
        if (!receipt.OwnedGenerations.Contains(pointer.GenerationLeaf, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var observed = AnnReceiptFiles.Read<AnnManifest>(
            AnnPaths.ExistingFile(root, pointer.GenerationLeaf, AnnProtocol.ManifestFile), options);
        AnnDigest.Require(pointer.ManifestSha256, observed.Digest);
        if (observed.Value.IsPending || observed.Value.Consumer != key.Consumer || observed.Value.IndexGeneration != key.IndexGeneration)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.InvalidSource);
        }
        var prior = receipt.Published.Where(item => item.Key != key).ToArray();
        if (prior.Length == options.MaximumOwnedGenerations)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        var next = receipt with
        {
            Published = [.. prior, new(key, pointer)],
            Pending = receipt.Pending.Where(item => item.Key != key).ToArray()
        };
        AnnRootInventory.Require(root, next, next.NodeId, next.Incarnation, options);
        AnnRootReceipts.Replace(root, next, options);
        return next;
    }

    internal static AnnRootReceipt Stage(string root, AnnRootReceipt receipt,
        AnnOwnedKey key, AnnPointer pointer, AnnExecutionOptions options)
    {
        AnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        AnnPaths.RequireClosedGeneration(root, pointer.GenerationLeaf);
        if (!receipt.OwnedGenerations.Contains(pointer.GenerationLeaf, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var observed = AnnReceiptFiles.Read<AnnManifest>(
            AnnPaths.ExistingFile(root, pointer.GenerationLeaf, AnnProtocol.ManifestFile), options);
        AnnDigest.Require(pointer.ManifestSha256, observed.Digest);
        if (!observed.Value.IsPending || observed.Value.Consumer != key.Consumer
            || observed.Value.IndexGeneration != key.IndexGeneration)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        var next = receipt with
        {
            Pending = [.. receipt.Pending.Where(item => item.Key != key), new(key, pointer)]
        };
        AnnRootInventory.Require(root, next, next.NodeId, next.Incarnation, options);
        AnnRootReceipts.Replace(root, next, options);
        return next;
    }

    internal static AnnRootReceipt PrepareUnpublish(AnnRootReceipt receipt,
        AnnOwnedKey key, bool releaseOriginal)
    {
        return receipt with
        {
            Published = receipt.Published.Where(item => item.Key != key).ToArray(),
            Pending = receipt.Pending.Where(item => item.Key != key).ToArray(),
            OriginalOperations = releaseOriginal ? AnnOriginalOperations.Release(receipt, key) : receipt.OriginalOperations
        };
    }

    internal static AnnRootReceipt Remove(string root, AnnRootReceipt receipt,
        string leaf, AnnExecutionOptions options)
    {
        AnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        if (!receipt.OwnedGenerations.Contains(leaf, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var retired = receipt with
        {
            Published = receipt.Published.Where(item => item.Pointer.GenerationLeaf != leaf).ToArray(),
            Pending = receipt.Pending.Where(item => item.Pointer.GenerationLeaf != leaf).ToArray()
        };
        AnnRootReceipts.Replace(root, retired, options);
        var directory = AnnPaths.Generation(root, leaf);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        var next = retired with
        {
            OwnedGenerations = retired.OwnedGenerations.Where(item => item != leaf).ToArray()
        };
        AnnRootReceipts.Replace(root, next, options);
        return next;
    }
}
