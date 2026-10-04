namespace KeyLoad.Server;

internal static class ServerNodeUpgradeProgressFile
{
    internal static void Seal(string stage, ServerNodeUpgradeOwner owner, NodeFormatUpgradeStage boundary,
        Action<NodeFormatUpgradeStage>? observer = null)
    {
        var inventory = Capture(stage);
        var temporary = Path.Combine(stage, ServerNodeUpgradeProtocol.ProgressReceipt + ".tmp");
        ServerNodeUpgradeReceiptFile.Write(temporary, new ServerNodeUpgradeProgress(
            ServerNodeUpgradeProtocol.ProgressFormatVersion, owner, inventory.Sha256, (int)boundary,
            owner.SourceEpoch, owner.TargetEpoch), ServerNodeUpgradeProtocol.ProgressMagic);
        File.Move(temporary, Path.Combine(stage, ServerNodeUpgradeProtocol.ProgressReceipt), overwrite: true);
        observer?.Invoke(boundary);
    }

    internal static void Verify(string stage, ServerNodeUpgradeOwner owner)
    {
        var progress = ReadBound(stage, owner);
        if (Capture(stage).Sha256 != progress.InventorySha256)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }

    internal static void VerifyCompleted(string directory, ServerNodeUpgradeOwner owner)
    {
        if (ReadBound(directory, owner).StageCode != (int)NodeFormatUpgradeStage.TargetVerified)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }

    private static ServerNodeUpgradeProgress ReadBound(string directory, ServerNodeUpgradeOwner owner)
    {
        var progress = ServerNodeUpgradeReceiptFile.Read<ServerNodeUpgradeProgress>(
            Path.Combine(directory, ServerNodeUpgradeProtocol.ProgressReceipt), ServerNodeUpgradeProtocol.ProgressMagic);
        if (progress.FormatVersion != ServerNodeUpgradeProtocol.ProgressFormatVersion
            || progress.SourceOwner != owner || progress.SourceEpoch != owner.SourceEpoch
            || progress.TargetEpoch != owner.TargetEpoch || progress.StageCode is < 0 or > 5
            || !ServerNodeUpgradeReceiptValidation.IsDigest(progress.InventorySha256))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        return progress;
    }

    private static ServerNodeUpgradeInventory Capture(string stage)
        => ServerNodeUpgradeInventory.Capture(stage, excludePreparedReceipt: true, excludeProgressReceipt: true);
}
