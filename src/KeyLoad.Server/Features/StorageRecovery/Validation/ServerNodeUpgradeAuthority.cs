using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server;

internal sealed record ServerNodeUpgradeAuthority(StoreIdentity Canonical, StoreIdentity Replica)
{
    internal static ServerNodeUpgradeOwner Bind(ServerNodeUpgradePaths paths, ServerNodeUpgradeInventory original,
        NodeOptions options)
    {
        if (original.Entries.Any(entry => entry.Path is ServerNodeUpgradeProtocol.CanonicalIdentityPath or ServerNodeUpgradeProtocol.ReplicaIdentityPath
            && entry.Length > 4_096))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        var authority = ServerNodeUpgradePrivateDirectory.Run(Path.GetDirectoryName(paths.Destination)!, directory =>
        {
            var inputs = Path.Combine(directory, ServerNodeUpgradeProtocol.Inputs);
            CopyInputs(paths.Source, inputs);
            return VerifyCopies(inputs, options);
        });
        var canonical = authority.Canonical;
        var replica = authority.Replica;
        if (canonical.FormatVersion != replica.FormatVersion
            || canonical.FormatVersion is not (ServerNodeUpgradeProtocol.Native5SourceEpoch
                or ServerNodeUpgradeProtocol.Native6SourceEpoch))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        return new(ServerNodeUpgradeProtocol.OwnerFormatVersion, paths.Source, paths.Destination, original.Sha256,
            original.FileDigest("database/identity.json"), original.FileDigest("database/commands.wal"),
            original.FileDigest("replica/identity.json"), original.FileDigest("replica/commands.wal"),
            canonical.FormatVersion, ServerNodeUpgradeProtocol.TargetEpoch);
    }

    internal static ServerNodeUpgradeAuthority VerifyCopies(string input, NodeOptions options)
        => new(ZoneTreeFormatUpgrade.VerifySource(StoreOptions(Path.Combine(input, ServerNodeUpgradeProtocol.Canonical), options)),
            ZoneTreeFormatUpgrade.VerifySource(StoreOptions(Path.Combine(input, ServerNodeUpgradeProtocol.Replica), options)));

    internal static ZoneTreeStoreOptions StoreOptions(string directory, NodeOptions options)
        => new(directory) { Incarnation = options.Incarnation, SigningKey = Convert.FromBase64String(options.SigningKey) };

    internal static void CopyInputs(string source, string inputs)
    {
        ServerNodeUpgradeFiles.CreatePrivateDirectory(inputs);
        CopyStore(source, inputs, ServerNodeUpgradeProtocol.Canonical);
        CopyStore(source, inputs, ServerNodeUpgradeProtocol.Replica);
    }

    internal static void CopyStore(string source, string inputs, string name)
    {
        var destination = Path.Combine(inputs, name);
        ServerNodeUpgradeFiles.CreatePrivateDirectory(destination);
        foreach (var file in new[] { ServerNodeUpgradeProtocol.Identity, ServerNodeUpgradeProtocol.Journal })
        { ServerNodeUpgradeFiles.Copy(Path.Combine(source, name, file), Path.Combine(destination, file)); }
        ServerNodeUpgradeFiles.CreateEmpty(Path.Combine(destination, ServerNodeUpgradeProtocol.StoreOwner));
    }
}
