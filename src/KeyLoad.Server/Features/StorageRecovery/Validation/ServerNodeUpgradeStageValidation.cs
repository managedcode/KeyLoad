namespace KeyLoad.Server;

internal static class ServerNodeUpgradeStageValidation
{
    internal static void VerifyReset(string stage, ServerNodeUpgradeOwner owner, NodeOptions options)
    {
        var inventory = ServerNodeUpgradeInventory.Capture(stage);
        ServerNodeUpgradeLayout.VerifyTarget(inventory, allowInputs: true);
        if (File.Exists(Path.Combine(stage, ServerNodeUpgradeProtocol.PreparedReceipt)))
        {
            var receipt = ServerNodeUpgradeStage.ReadPrepared(stage);
            ServerNodeUpgradeReceiptValidation.Verify(owner, receipt, options);
            if (ServerNodeUpgradeInventory.Capture(stage, excludePreparedReceipt: true).Sha256
                != receipt.PreparedTargetInventorySha256)
            { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
            return;
        }
        if (File.Exists(Path.Combine(stage, ServerNodeUpgradeProtocol.ProgressReceipt)))
        {
            ServerNodeUpgradeProgressFile.Verify(stage, owner);
            return;
        }
        foreach (var entry in inventory.Entries)
        {
            if (entry.Path is ServerNodeUpgradeProtocol.OwnerReceipt or ServerNodeUpgradeProtocol.NodeOwner
                || entry.Path == ServerNodeUpgradeProtocol.Inputs
                || entry.Path.StartsWith(ServerNodeUpgradeProtocol.Inputs + ServerNodeUpgradeProtocol.PathSeparator, StringComparison.Ordinal))
            { continue; }
            throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid);
        }
        var inputs = Path.Combine(stage, ServerNodeUpgradeProtocol.Inputs);
        if (Directory.Exists(inputs))
        { ServerNodeUpgradeInputValidation.Verify(inputs, owner, allowPartial: true); }
    }
}
