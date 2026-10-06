using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed record ServerNodeUpgradeAuthority(StoreIdentity Canonical, StoreIdentity Replica)
{
    private const string ReplicaCommandsJournalPath = "replica/commands.wal";

    internal static ServerNodeUpgradeOwner Bind(ServerNodeUpgradePaths paths, ServerNodeUpgradeInventory original,
        ServerRuntimeOptions options)
    {
        const int EntryLengthValidationBoundary = 4_096;
        const string BindPathText = "database/identity.json";
        const string BindBindPathText = "database/commands.wal";

        if (original.Entries.Any(entry => entry.Path is ServerNodeUpgradeProtocol.CanonicalIdentityPath or ServerNodeUpgradeProtocol.ReplicaIdentityPath
            && entry.Length > EntryLengthValidationBoundary))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        var authority = ServerNodeUpgradePrivateDirectory.Run(Path.GetDirectoryName(paths.Destination)!, directory =>
        {
            var inputs = Path.Combine(directory, ServerNodeUpgradeProtocol.Inputs);
            CopyInputs(paths.Source, inputs, executionOptions: options.NodeUpgrade);
            return VerifyCopies(inputs, options);
        });
        var canonical = authority.Canonical;
        var replica = authority.Replica;
        if (canonical.FormatVersion != replica.FormatVersion
            || canonical.FormatVersion is not (ServerNodeUpgradeProtocol.Native5SourceEpoch
                or ServerNodeUpgradeProtocol.Native6SourceEpoch))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        return new(ServerNodeUpgradeProtocol.OwnerFormatVersion, paths.Source, paths.Destination, original.Sha256,
            original.FileDigest(BindPathText), original.FileDigest(BindBindPathText),
            original.FileDigest(ServerNodeUpgradeProtocol.ReplicaIdentityPath), original.FileDigest(ReplicaCommandsJournalPath),
            canonical.FormatVersion, ServerNodeUpgradeProtocol.TargetEpoch);
    }

    internal static ServerNodeUpgradeAuthority VerifyCopies(string input, ServerRuntimeOptions options)
        => new(ZoneTreeFormatUpgrade.VerifySource(StoreOptions(Path.Combine(input, ServerNodeUpgradeProtocol.Canonical), options), options.StorageExecution),
            ZoneTreeFormatUpgrade.VerifySource(StoreOptions(Path.Combine(input, ServerNodeUpgradeProtocol.Replica), options), options.StorageExecution));

    internal static ZoneTreeStoreOptions StoreOptions(string directory, ServerRuntimeOptions options)
        => new ZoneTreeStoreOptions(directory)
        {
            Incarnation = options.Node.Value.Incarnation,
            SigningKey = Convert.FromBase64String(options.Node.Value.SigningKey)
        }.ResolveExecutionOptions(options.StorageExecution);

    internal static void CopyInputs(string source, string inputs, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        ServerNodeUpgradeFiles.CreatePrivateDirectory(inputs);
        CopyStore(source, inputs, ServerNodeUpgradeProtocol.Canonical, executionOptions: executionOptions);
        CopyStore(source, inputs, ServerNodeUpgradeProtocol.Replica, executionOptions: executionOptions);
    }

    internal static void CopyStore(string source, string inputs, string name, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        var destination = Path.Combine(inputs, name);
        ServerNodeUpgradeFiles.CreatePrivateDirectory(destination);
        foreach (var file in new[] { ServerNodeUpgradeProtocol.Identity, ServerNodeUpgradeProtocol.Journal })
        { ServerNodeUpgradeFiles.Copy(Path.Combine(source, name, file), Path.Combine(destination, file), executionOptions: executionOptions); }
        ServerNodeUpgradeFiles.CreateEmpty(Path.Combine(destination, ServerNodeUpgradeProtocol.StoreOwner), executionOptions: executionOptions);
    }
}
