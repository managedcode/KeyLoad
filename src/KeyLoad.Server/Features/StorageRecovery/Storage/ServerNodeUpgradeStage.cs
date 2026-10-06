using Microsoft.Extensions.Options;
namespace KeyLoad.Server;

internal static class ServerNodeUpgradeStage
{
    internal static void CreateOrReset(ServerNodeUpgradePaths paths, ServerNodeUpgradeOwner owner, ServerRuntimeOptions options)
    {
        if (File.Exists(paths.Stage))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        if (Directory.Exists(paths.Stage))
        {
            var existing = ReadOwner(paths.Stage, executionOptions: options.NodeUpgrade);
            if (existing != owner)
            { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
            ServerNodeUpgradeStageValidation.VerifyReset(paths.Stage, owner, options.Node.Value, executionOptions: options.NodeUpgrade);
            Directory.Delete(paths.Stage, recursive: true);
        }
        ServerNodeUpgradeFiles.CreatePrivateDirectory(paths.Stage);
        ServerNodeUpgradeReceiptFile.Write(Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.OwnerReceipt), owner,
            ServerNodeUpgradeProtocol.OwnerMagic, executionOptions: options.NodeUpgrade);
        ServerNodeUpgradeFiles.CreateEmpty(Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.NodeOwner), executionOptions: options.NodeUpgrade);
    }

    internal static ServerNodeUpgradeOwner ReadOwner(string directory, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
        => ServerNodeUpgradeReceiptFile.Read<ServerNodeUpgradeOwner>(
            Path.Combine(directory, ServerNodeUpgradeProtocol.OwnerReceipt), ServerNodeUpgradeProtocol.OwnerMagic, executionOptions: executionOptions);

    internal static ServerNodeUpgradeReceipt ReadPrepared(string directory, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
        => ServerNodeUpgradeReceiptFile.Read<ServerNodeUpgradeReceipt>(
            Path.Combine(directory, ServerNodeUpgradeProtocol.PreparedReceipt), ServerNodeUpgradeProtocol.PreparedMagic, executionOptions: executionOptions);

    internal static void RemoveInputs(string stage, ServerNodeUpgradeOwner owner, ServerRuntimeOptions options)
    {
        var inputs = Path.Combine(stage, ServerNodeUpgradeProtocol.Inputs);
        ServerNodeUpgradeInputValidation.Verify(inputs, owner, executionOptions: options.NodeUpgrade);
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

    internal static void RequireOwnedPublishedTarget(string destination, ServerNodeUpgradeOwner owner, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        if (!File.Exists(Path.Combine(destination, ServerNodeUpgradeProtocol.OwnerReceipt))
            || !File.Exists(Path.Combine(destination, ServerNodeUpgradeProtocol.PreparedReceipt))
            || ReadOwner(destination, executionOptions: executionOptions) != owner)
        { throw Errors.Fail(ErrorCode.Conflict, ServerNodeUpgradeProtocol.Invalid); }
    }
}
