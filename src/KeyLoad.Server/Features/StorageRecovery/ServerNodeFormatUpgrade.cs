namespace KeyLoad.Server;

/// <summary>Prepares and separately publishes one fully stopped physical native5 node.</summary>
internal static class ServerNodeFormatUpgrade
{
    internal static ServerNodeUpgradeReceipt Prepare(string source, NodeOptions destinationOptions,
        Action<NodeFormatUpgradeStage>? observer = null)
    {
        var paths = Validate(source, destinationOptions);
        using var sourceLocks = ServerNodeUpgradeLocks.Acquire(paths.Source);
        var original = ReadSource(paths.Source, sourceLocks);
        var owner = ServerNodeUpgradeAuthority.Bind(paths, original);
        if (Directory.Exists(paths.Destination))
        { return VerifyPublished(paths, destinationOptions, original, owner, sourceLocks); }
        ServerNodeUpgradeStage.RequireAbsentTarget(paths.Destination);
        ServerNodeUpgradeStage.CreateOrReset(paths, owner, destinationOptions);
        var receipt = ServerNodeUpgradeBuilder.Build(paths, destinationOptions, original, owner, observer);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(paths.Source, sourceLocks.Held));
        _ = VerifyPreparedTarget(paths.Stage, owner, destinationOptions, published: false);
        observer?.Invoke(NodeFormatUpgradeStage.TargetVerified);
        return receipt;
    }

    internal static ServerNodeUpgradeReceipt VerifyPrepared(string source, NodeOptions destinationOptions)
    {
        var paths = Validate(source, destinationOptions);
        using var sourceLocks = ServerNodeUpgradeLocks.Acquire(paths.Source);
        var original = ReadSource(paths.Source, sourceLocks);
        var owner = ServerNodeUpgradeAuthority.Bind(paths, original);
        if (Directory.Exists(paths.Destination))
        { return VerifyPublished(paths, destinationOptions, original, owner, sourceLocks); }
        var receipt = VerifyPreparedTarget(paths.Stage, owner, destinationOptions, published: false);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(paths.Source, sourceLocks.Held));
        return receipt;
    }

    internal static ServerNodeUpgradeReceipt Publish(string source, NodeOptions destinationOptions,
        Action<NodeFormatUpgradeStage>? observer = null)
    {
        var paths = Validate(source, destinationOptions);
        using var sourceLocks = ServerNodeUpgradeLocks.Acquire(paths.Source);
        var original = ReadSource(paths.Source, sourceLocks);
        var owner = ServerNodeUpgradeAuthority.Bind(paths, original);
        if (Directory.Exists(paths.Destination))
        { return VerifyPublished(paths, destinationOptions, original, owner, sourceLocks); }
        using var targetLocks = ServerNodeUpgradeLocks.Acquire(paths.Stage);
        var receipt = VerifyLockedTarget(paths.Stage, owner, destinationOptions, targetLocks, published: false);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(paths.Source, sourceLocks.Held));
        ServerNodeUpgradeStage.RequireAbsentTarget(paths.Destination);
        Directory.Move(paths.Stage, paths.Destination);
        observer?.Invoke(NodeFormatUpgradeStage.Published);
        return receipt;
    }

    private static ServerNodeUpgradePaths Validate(string source, NodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return ServerNodeUpgradePaths.Create(source, options.DataDirectory);
    }

    private static ServerNodeUpgradeInventory ReadSource(string source, ServerNodeUpgradeLocks locks)
    {
        var inventory = ServerNodeUpgradeInventory.Capture(source, locks.Held);
        ServerNodeUpgradeLayout.VerifySource(inventory);
        return inventory;
    }

    private static ServerNodeUpgradeReceipt VerifyPublished(ServerNodeUpgradePaths paths, NodeOptions options,
        ServerNodeUpgradeInventory original, ServerNodeUpgradeOwner owner, ServerNodeUpgradeLocks sourceLocks)
    {
        ServerNodeUpgradeStage.RequireOwnedPublishedTarget(paths.Destination, owner);
        var receipt = VerifyPreparedTarget(paths.Destination, owner, options, published: true);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(paths.Source, sourceLocks.Held));
        return receipt;
    }

    private static ServerNodeUpgradeReceipt VerifyPreparedTarget(string directory, ServerNodeUpgradeOwner owner,
        NodeOptions options, bool published)
    {
        using var locks = ServerNodeUpgradeLocks.Acquire(directory);
        return VerifyLockedTarget(directory, owner, options, locks, published);
    }

    private static ServerNodeUpgradeReceipt VerifyLockedTarget(string directory, ServerNodeUpgradeOwner owner,
        NodeOptions options, ServerNodeUpgradeLocks locks, bool published)
    {
        var original = ServerNodeUpgradeInventory.Capture(directory, locks.Held);
        ServerNodeUpgradeLayout.VerifyTarget(original, allowInputs: false);
        if (ServerNodeUpgradeStage.ReadOwner(directory) != owner)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        var receipt = ServerNodeUpgradeStage.ReadPrepared(directory);
        ServerNodeUpgradeReceiptValidation.Verify(owner, receipt, options);
        ServerNodeUpgradeProgressFile.VerifyCompleted(directory, owner);
        if (!published && ServerNodeUpgradeInventory.Capture(directory, locks.Held, excludePreparedReceipt: true).Sha256
            != receipt.PreparedTargetInventorySha256)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        ServerNodeUpgradeVerifier.Verify(directory, receipt, options, published);
        original.RequireSame(ServerNodeUpgradeInventory.Capture(directory, locks.Held));
        return receipt;
    }
}
