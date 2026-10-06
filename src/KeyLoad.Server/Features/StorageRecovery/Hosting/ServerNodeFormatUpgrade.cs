using Microsoft.Extensions.Options;
namespace KeyLoad.Server;

/// <summary>Prepares and separately publishes one fully stopped physical native5 node.</summary>
internal static class ServerNodeFormatUpgrade
{
    internal static ServerNodeUpgradeReceipt Prepare(string source, ServerRuntimeOptions destinationOptions,
        Action<NodeFormatUpgradeStage>? observer = null)
    {
        var paths = Validate(source, destinationOptions);
        using var sourceLocks = ServerNodeUpgradeLocks.Acquire(paths.Source, executionOptions: destinationOptions.NodeUpgrade);
        var original = ReadSource(paths.Source, sourceLocks, executionOptions: destinationOptions.NodeUpgrade);
        var owner = ServerNodeUpgradeAuthority.Bind(paths, original, destinationOptions);
        if (Directory.Exists(paths.Destination))
        { return VerifyPublished(paths, destinationOptions, original, owner, sourceLocks); }
        ServerNodeUpgradeStage.RequireAbsentTarget(paths.Destination);
        ServerNodeUpgradeStage.CreateOrReset(paths, owner, destinationOptions);
        var receipt = ServerNodeUpgradeBuilder.Build(paths, destinationOptions, original, owner, observer);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(paths.Source, held: sourceLocks.Held, executionOptions: destinationOptions.NodeUpgrade));
        _ = VerifyPreparedTarget(paths.Stage, owner, destinationOptions, published: false);
        observer?.Invoke(NodeFormatUpgradeStage.TargetVerified);
        return receipt;
    }

    internal static ServerNodeUpgradeReceipt VerifyPrepared(string source, ServerRuntimeOptions destinationOptions)
    {
        var paths = Validate(source, destinationOptions);
        using var sourceLocks = ServerNodeUpgradeLocks.Acquire(paths.Source, executionOptions: destinationOptions.NodeUpgrade);
        var original = ReadSource(paths.Source, sourceLocks, executionOptions: destinationOptions.NodeUpgrade);
        var owner = ServerNodeUpgradeAuthority.Bind(paths, original, destinationOptions);
        if (Directory.Exists(paths.Destination))
        { return VerifyPublished(paths, destinationOptions, original, owner, sourceLocks); }
        var receipt = VerifyPreparedTarget(paths.Stage, owner, destinationOptions, published: false);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(paths.Source, held: sourceLocks.Held, executionOptions: destinationOptions.NodeUpgrade));
        return receipt;
    }

    internal static ServerNodeUpgradeReceipt Publish(string source, ServerRuntimeOptions destinationOptions,
        Action<NodeFormatUpgradeStage>? observer = null)
    {
        var paths = Validate(source, destinationOptions);
        using var sourceLocks = ServerNodeUpgradeLocks.Acquire(paths.Source, executionOptions: destinationOptions.NodeUpgrade);
        var original = ReadSource(paths.Source, sourceLocks, executionOptions: destinationOptions.NodeUpgrade);
        var owner = ServerNodeUpgradeAuthority.Bind(paths, original, destinationOptions);
        if (Directory.Exists(paths.Destination))
        { return VerifyPublished(paths, destinationOptions, original, owner, sourceLocks); }
        using var targetLocks = ServerNodeUpgradeLocks.Acquire(paths.Stage, executionOptions: destinationOptions.NodeUpgrade);
        var receipt = VerifyLockedTarget(paths.Stage, owner, destinationOptions, targetLocks, published: false);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(paths.Source, held: sourceLocks.Held, executionOptions: destinationOptions.NodeUpgrade));
        ServerNodeUpgradeStage.RequireAbsentTarget(paths.Destination);
        Directory.Move(paths.Stage, paths.Destination);
        observer?.Invoke(NodeFormatUpgradeStage.Published);
        return receipt;
    }

    private static ServerNodeUpgradePaths Validate(string source, ServerRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ValidateBeforePhysicalOwnership();
        return ServerNodeUpgradePaths.Create(source, options.Node.Value.DataDirectory);
    }

    private static ServerNodeUpgradeInventory ReadSource(string source, ServerNodeUpgradeLocks locks, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        var inventory = ServerNodeUpgradeInventory.Capture(source, held: locks.Held, executionOptions: executionOptions);
        ServerNodeUpgradeLayout.VerifySource(inventory);
        return inventory;
    }

    private static ServerNodeUpgradeReceipt VerifyPublished(ServerNodeUpgradePaths paths, ServerRuntimeOptions options,
        ServerNodeUpgradeInventory original, ServerNodeUpgradeOwner owner, ServerNodeUpgradeLocks sourceLocks)
    {
        ServerNodeUpgradeStage.RequireOwnedPublishedTarget(paths.Destination, owner, executionOptions: options.NodeUpgrade);
        var receipt = VerifyPreparedTarget(paths.Destination, owner, options, published: true);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(paths.Source, held: sourceLocks.Held, executionOptions: options.NodeUpgrade));
        return receipt;
    }

    private static ServerNodeUpgradeReceipt VerifyPreparedTarget(string directory, ServerNodeUpgradeOwner owner,
        ServerRuntimeOptions options, bool published)
    {
        using var locks = ServerNodeUpgradeLocks.Acquire(directory, executionOptions: options.NodeUpgrade);
        return VerifyLockedTarget(directory, owner, options, locks, published);
    }

    private static ServerNodeUpgradeReceipt VerifyLockedTarget(string directory, ServerNodeUpgradeOwner owner,
        ServerRuntimeOptions options, ServerNodeUpgradeLocks locks, bool published)
    {
        var original = ServerNodeUpgradeInventory.Capture(directory, held: locks.Held, executionOptions: options.NodeUpgrade);
        ServerNodeUpgradeLayout.VerifyTarget(original, allowInputs: false);
        if (ServerNodeUpgradeStage.ReadOwner(directory, executionOptions: options.NodeUpgrade) != owner)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        var receipt = ServerNodeUpgradeStage.ReadPrepared(directory, executionOptions: options.NodeUpgrade);
        ServerNodeUpgradeReceiptValidation.Verify(owner, receipt, options.Node.Value);
        ServerNodeUpgradeProgressFile.VerifyCompleted(directory, owner, executionOptions: options.NodeUpgrade);
        if (!published && ServerNodeUpgradeInventory.Capture(directory, held: locks.Held, excludePreparedReceipt: true, executionOptions: options.NodeUpgrade).Sha256
            != receipt.PreparedTargetInventorySha256)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        _ = ServerNodeUpgradeVerifier.Verify(directory, receipt, options, published);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(directory, held: locks.Held, executionOptions: options.NodeUpgrade));
        return receipt;
    }
}
