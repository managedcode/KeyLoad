namespace KeyLoad.Server;

internal static class ServerNodeUpgradeStage
{
    internal static void CreateOrReset(ServerNodeUpgradePaths paths, ServerNodeUpgradeOwner owner, ServerRuntimeOptions options)
    {
        if (File.Exists(paths.Stage))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        if (Directory.Exists(paths.Stage))
        {
            var existing = ReadOwner(paths.Stage);
            if (existing != owner)
            { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
            ServerNodeUpgradeStageValidation.VerifyReset(paths.Stage, owner, options.Node.Value);
            Directory.Delete(paths.Stage, recursive: true);
        }
        ServerNodeUpgradeFiles.CreatePrivateDirectory(paths.Stage);
        ServerNodeUpgradeReceiptFile.Write(Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.OwnerReceipt), owner,
            ServerNodeUpgradeProtocol.OwnerMagic);
        ServerNodeUpgradeFiles.CreateEmpty(Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.NodeOwner));
    }

    internal static ServerNodeUpgradeOwner ReadOwner(string directory)
        => ServerNodeUpgradeReceiptFile.Read<ServerNodeUpgradeOwner>(
            Path.Combine(directory, ServerNodeUpgradeProtocol.OwnerReceipt), ServerNodeUpgradeProtocol.OwnerMagic);

    internal static ServerNodeUpgradeReceipt ReadPrepared(string directory)
        => ServerNodeUpgradeReceiptFile.Read<ServerNodeUpgradeReceipt>(
            Path.Combine(directory, ServerNodeUpgradeProtocol.PreparedReceipt), ServerNodeUpgradeProtocol.PreparedMagic);

    internal static void RemoveInputs(string stage, ServerNodeUpgradeOwner owner, ServerRuntimeOptions options)
    {
        var inputs = Path.Combine(stage, ServerNodeUpgradeProtocol.Inputs);
        ServerNodeUpgradeInputValidation.Verify(inputs, owner);
        foreach (var name in new[] { ServerNodeUpgradeProtocol.Canonical, ServerNodeUpgradeProtocol.Replica })
        {
            KeyLoad.Storage.ZoneTree.ZoneTreeFormatUpgrade.RemoveOwnedReceipt(Path.Combine(inputs, name),
                ServerNodeUpgradeAuthority.StoreOptions(Path.Combine(stage, name), options), options.StorageExecution);
        }
        Directory.Delete(inputs, recursive: true);
    }

    internal static void RequireAbsentTarget(string destination)
    {
        ServerNodeUpgradePaths.CheckAncestors(destination, true);
        if (File.Exists(destination) || Directory.Exists(destination))
        { throw Errors.Fail(ErrorCode.Conflict, ServerNodeUpgradeProtocol.Invalid); }
    }

    internal static void RequireOwnedPublishedTarget(string destination, ServerNodeUpgradeOwner owner)
    {
        if (!File.Exists(Path.Combine(destination, ServerNodeUpgradeProtocol.OwnerReceipt))
            || !File.Exists(Path.Combine(destination, ServerNodeUpgradeProtocol.PreparedReceipt))
            || ReadOwner(destination) != owner)
        { throw Errors.Fail(ErrorCode.Conflict, ServerNodeUpgradeProtocol.Invalid); }
    }
}
