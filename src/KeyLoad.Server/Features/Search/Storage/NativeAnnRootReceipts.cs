namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnRootReceipts
{
    internal static NativeAnnRootReceipt Open(string root, Guid nodeId, Guid incarnation,
        NativeAnnExecutionOptions options)
    {
        var receiptPath = Path.Combine(root, NativeAnnProtocol.RootReceipt);
        var pendingPath = Path.Combine(root, NativeAnnProtocol.RootPending);
        if (File.Exists(pendingPath))
        {
            var pending = NativeAnnReceiptFiles.Read<NativeAnnRootReceipt>(pendingPath, options).Value;
            NativeAnnRootInventory.Require(root, pending, nodeId, incarnation, options);
            File.Move(pendingPath, receiptPath, overwrite: true);
        }
        if (File.Exists(receiptPath))
        {
            var receipt = NativeAnnReceiptFiles.Read<NativeAnnRootReceipt>(receiptPath, options).Value;
            NativeAnnRootInventory.Require(root, receipt, nodeId, incarnation, options);
            return receipt;
        }
        if (Directory.EnumerateFileSystemEntries(root).Any(path => Path.GetFileName(path) != NativeAnnProtocol.RootLock))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var created = new NativeAnnRootReceipt(NativeAnnProtocol.Version, root, nodeId, incarnation, [], [], []);
        NativeAnnReceiptFiles.Write(receiptPath, created, options);
        return created;
    }

    internal static NativeAnnRootReceipt Enroll(string root, NativeAnnRootReceipt receipt,
        string leaf, NativeAnnExecutionOptions options)
    {
        NativeAnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        NativeAnnPaths.Generation(root, leaf);
        if (receipt.OwnedGenerations.Contains(leaf, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        if (receipt.OwnedGenerations.Length == options.MaximumOwnedGenerations)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        var next = receipt with { OwnedGenerations = [.. receipt.OwnedGenerations, leaf] };
        Replace(root, next, options);
        return next;
    }

    internal static void Replace(string root, NativeAnnRootReceipt receipt, NativeAnnExecutionOptions options)
    {
        var pending = Path.Combine(root, NativeAnnProtocol.RootPending);
        NativeAnnReceiptFiles.Write(pending, receipt, options);
        File.Move(pending, Path.Combine(root, NativeAnnProtocol.RootReceipt), overwrite: true);
    }
}
