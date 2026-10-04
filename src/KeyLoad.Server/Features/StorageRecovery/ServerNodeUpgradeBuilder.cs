using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeBuilder
{
    internal static ServerNodeUpgradeReceipt Build(ServerNodeUpgradePaths paths, NodeOptions options,
        ServerNodeUpgradeInventory original, ServerNodeUpgradeOwner owner, Action<NodeFormatUpgradeStage>? observer)
    {
        var inputs = Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.Inputs);
        ServerNodeUpgradeAuthority.CopyInputs(paths.Source, inputs);
        var authority = ServerNodeUpgradeAuthority.VerifyCopies(inputs, options);
        if (authority.Canonical.NodeId == authority.Replica.NodeId)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        ServerNodeUpgradeProgressFile.Seal(paths.Stage, owner, NodeFormatUpgradeStage.SourceVerified, observer);
        ConvertStores(inputs, paths.Stage, options);
        ServerNodeUpgradeProgressFile.Seal(paths.Stage, owner, NodeFormatUpgradeStage.StoresConverted, observer);
        var preparation = Preflight(paths, options, original, owner);
        var images = PrepareImages(paths, options, original);
        ServerNodeUpgradeProgressFile.Seal(paths.Stage, owner, NodeFormatUpgradeStage.ImagesConverted, observer);
        PublishImages(preparation.Plan, paths, options, images);
        ServerNodeUpgradeProgressFile.Seal(paths.Stage, owner, NodeFormatUpgradeStage.DescriptorFlushed, observer);
        ServerNodeUpgradeStage.RemoveInputs(paths.Stage, owner, options);
        Directory.Delete(Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.PreparedImages));
        ServerNodeUpgradeLayout.VerifyTarget(ServerNodeUpgradeInventory.Capture(paths.Stage), allowInputs: false);
        ServerNodeUpgradeProgressFile.Seal(paths.Stage, owner, NodeFormatUpgradeStage.TargetVerified);
        var prepared = preparation.Receipt with
        {
            PreparedTargetInventorySha256 = ServerNodeUpgradeInventory.Capture(paths.Stage, excludePreparedReceipt: true).Sha256
        };
        ServerNodeUpgradeReceiptFile.Write(Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.PreparedReceipt), prepared,
            ServerNodeUpgradeProtocol.PreparedMagic);
        return prepared;
    }

    private static void ConvertStores(string inputs, string stage, NodeOptions options)
    {
        foreach (var name in new[] { ServerNodeUpgradeProtocol.Canonical, ServerNodeUpgradeProtocol.Replica })
        {
            _ = ZoneTreeFormatUpgrade.Upgrade(Path.Combine(inputs, name),
                ServerNodeUpgradeAuthority.StoreOptions(Path.Combine(stage, name), options));
        }
    }

    private static ServerNodeUpgradePreparation Preflight(ServerNodeUpgradePaths paths, NodeOptions options,
        ServerNodeUpgradeInventory original, ServerNodeUpgradeOwner owner)
        => ServerNodeUpgradeStores.Run(paths.Stage, options, stores =>
        {
            var database = new DatabaseEngine(stores.Canonical, new AuthorizationPolicy());
            var configuration = options.CreateReplicaConfiguration(paths.Stage);
            var plan = ReplicaSnapshotFormatUpgrade.Preflight(database, stores.Replica, configuration,
                Path.Combine(paths.Source, ServerNodeUpgradeProtocol.Snapshots),
                path => ZoneTreeSnapshotFormatUpgrade.VerifySource(path, options.Incarnation));
            using var log = new DurableReplicaLog(stores.Replica, configuration, canonicalDatabase: database);
            return new ServerNodeUpgradePreparation(plan,
                CreateReceipt(paths, original, owner, database, stores.Replica, log.State));
        });

    private static Dictionary<string, StorageSnapshot> PrepareImages(ServerNodeUpgradePaths paths,
        NodeOptions options, ServerNodeUpgradeInventory original)
    {
        var directory = Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.PreparedImages);
        ServerNodeUpgradeFiles.CreatePrivateDirectory(directory);
        var images = new Dictionary<string, StorageSnapshot>(StringComparer.Ordinal);
        foreach (var image in original.Entries.Where(entry => !entry.Directory
            && entry.Path.StartsWith(ServerNodeUpgradeProtocol.Snapshots + "/", StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(image.Path);
            images.Add(name, ZoneTreeSnapshotFormatUpgrade.Upgrade(Path.Combine(paths.Source, image.Path),
                Path.Combine(directory, name), options.Incarnation));
        }
        return images;
    }

    private static void PublishImages(ReplicaSnapshotUpgradePlan plan, ServerNodeUpgradePaths paths, NodeOptions options,
        Dictionary<string, StorageSnapshot> images)
        => ServerNodeUpgradeStores.Run(paths.Stage, options, stores =>
        {
            var database = new DatabaseEngine(stores.Canonical, new AuthorizationPolicy());
            ReplicaSnapshotFormatUpgrade.Upgrade(plan, database, stores.Replica,
                options.CreateReplicaConfiguration(paths.Stage), Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.Snapshots),
                (source, destination) =>
                {
                    var name = Path.GetFileName(source);
                    var cut = images[name];
                    File.Move(Path.Combine(paths.Stage, ServerNodeUpgradeProtocol.PreparedImages, name), destination);
                    return cut;
                });
            return true;
        });

    private static ServerNodeUpgradeReceipt CreateReceipt(ServerNodeUpgradePaths paths,
        ServerNodeUpgradeInventory original, ServerNodeUpgradeOwner owner, DatabaseEngine canonical,
        ZoneTreeStore replica, ReplicaHardState state)
        => new(1, paths.Source, paths.Destination, original.Sha256, owner.CanonicalIdentitySha256,
            owner.CanonicalJournalSha256, owner.ReplicaIdentitySha256, owner.ReplicaJournalSha256,
            canonical.Store.Identity.NodeId, replica.Identity.NodeId, canonical.Store.Identity.Incarnation,
            ServerNodeUpgradeProtocol.SourceEpoch, ServerNodeUpgradeProtocol.TargetEpoch,
            canonical.Store.Position, canonical.LastApplied, replica.Position, canonical.Store.Identity.ReadGeneration,
            replica.Identity.ReadGeneration, state, string.Empty, original.BackupCount);
}

internal sealed record ServerNodeUpgradePreparation(ReplicaSnapshotUpgradePlan Plan, ServerNodeUpgradeReceipt Receipt);
