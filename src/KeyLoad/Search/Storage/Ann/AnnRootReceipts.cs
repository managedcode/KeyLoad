namespace KeyLoad.Server.Features.Search;

internal static class AnnRootReceipts
{
    internal static AnnRootReceipt Open(string root, Guid nodeId, Guid incarnation,
        AnnExecutionOptions options)
    {
        var receiptPath = Path.Combine(root, AnnProtocol.RootReceipt);
        var pendingPath = Path.Combine(root, AnnProtocol.RootPending);
        if (File.Exists(pendingPath))
        {
            var pending = AnnReceiptFiles.Read<AnnRootReceipt>(pendingPath, options).Value;
            AnnRootInventory.Require(root, pending, nodeId, incarnation, options);
            File.Move(pendingPath, receiptPath, overwrite: true);
        }
        if (File.Exists(receiptPath))
        {
            var receipt = AnnReceiptFiles.Read<AnnRootReceipt>(receiptPath, options).Value;
            AnnRootInventory.Require(root, receipt, nodeId, incarnation, options);
            return receipt;
        }
        if (Directory.EnumerateFileSystemEntries(root).Any(path => Path.GetFileName(path) != AnnProtocol.RootLock))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var created = new AnnRootReceipt(AnnProtocol.Version, root, nodeId, incarnation, [], [], [], []);
        AnnReceiptFiles.Write(receiptPath, created, options);
        return created;
    }

    internal static AnnRootReceipt Enroll(string root, AnnRootReceipt receipt,
        string leaf, AnnExecutionOptions options)
    {
        AnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        AnnPaths.Generation(root, leaf);
        if (receipt.OwnedGenerations.Contains(leaf, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        if (receipt.OwnedGenerations.Length == options.MaximumOwnedGenerations)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        var next = receipt with
        {
            OwnedGenerations = [.. receipt.OwnedGenerations, leaf]
        };
        Replace(root, next, options);
        return next;
    }

    internal static void Replace(string root, AnnRootReceipt receipt, AnnExecutionOptions options)
    {
        var pending = Path.Combine(root, AnnProtocol.RootPending);
        if (global::KeyLoad.OrleansSerialization.Measure(receipt) > options.MaximumManifestBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        AnnReceiptFiles.Write(pending, receipt, options);
        File.Move(pending, Path.Combine(root, AnnProtocol.RootReceipt), overwrite: true);
    }
}
