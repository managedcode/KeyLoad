using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Operates only on one stopped fixture-owned RF3 voter and genuine native persisted cuts.</summary>
internal static class EmptyReplicaSnapshotStorage
{
    private const string Canonical = "database";
    private const string NativeTextProjection = "search-indexes";
    private const string IncrementalTextProjection = "text-projections";
    private const string NodeOwner = "node.owner.lock";
    private const string StoreOwner = "owner.lock";
    private const string Journal = "commands.wal";
    private const string Metadata = "tree/0.meta.wal";
    private static readonly string[] ErasedDirectories = [Canonical, ReplicaProtocol.ReplicaDirectory, ReplicaProtocol.SnapshotDirectory,
        NativeTextProjection, IncrementalTextProjection];

    internal static void Erase(ClusterFixture fixture, string node)
    {
        var root = RequireRoot(fixture, node);
        NodeEpochRf3OfflineFiles.RequireRegular(Path.Combine(root, NodeOwner));
        using var owner = File.Open(Path.Combine(root, NodeOwner), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        foreach (var directory in ErasedDirectories)
        {
            var path = Path.Combine(root, directory);
            RequireTree(path);
            if (directory is Canonical or ReplicaProtocol.ReplicaDirectory)
            {
                NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(path, StoreOwner));
                NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(path, Journal));
                var metadata = Path.Combine(path, Metadata);
                if (File.Exists(metadata))
                { NodeEpochRf3OfflineFiles.AssertExclusive(metadata); }
            }
        }
        foreach (var directory in ErasedDirectories)
        {
            Directory.Delete(Path.Combine(root, directory), recursive: true);
            if (Directory.Exists(Path.Combine(root, directory)))
            { throw new InvalidOperationException("The stopped follower storage was not erased."); }
        }
    }

    internal static ReplicaHardState ReadInstalledState(ClusterFixture fixture, string node)
    {
        var root = RequireRoot(fixture, node);
        NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, NodeOwner));
        using var replica = new ZoneTreeStore(new(Path.Combine(root, ReplicaProtocol.ReplicaDirectory)),
            IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
        return replica.Read(view =>
        {
            var bytes = view.ReadOwnedValue(KeyCodec.Encode(ReplicaProtocol.StateKey))
                ?? throw new InvalidOperationException("The actual recovered replica metadata is absent.");
            var state = ReplicaProtocolCodec.Deserialize<ReplicaHardState>(bytes);
            var snapshot = state.Snapshot
                ?? throw new InvalidOperationException("The actual installed snapshot is absent.");
            var prefix = view.VisitRange(KeyCodec.Encode(ReplicaProtocol.EntryKey), 1, static (_, _) => false,
                untilKey: ReplicaProtocol.EntryStorageKey(checked(snapshot.Index + 1L)));
            if (prefix.Records > 0)
            { throw new InvalidOperationException("The actual installed checkpoint prefix has not settled before the later tail."); }
            return state;
        });
    }

    internal static EmptyReplicaSnapshotCut ReadReplicaState(ClusterFixture fixture, string node, long tailPosition)
    {
        var root = RequireRoot(fixture, node);
        NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, NodeOwner));
        using var replica = new ZoneTreeStore(new(Path.Combine(root, ReplicaProtocol.ReplicaDirectory)),
            IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
        return replica.Read(view =>
        {
            var bytes = view.ReadOwnedValue(KeyCodec.Encode(ReplicaProtocol.StateKey))
                ?? throw new InvalidOperationException("The actual recovered replica metadata is absent.");
            var state = ReplicaProtocolCodec.Deserialize<ReplicaHardState>(bytes);
            var entry = view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(tailPosition));
            if (entry is null)
            {
                var failure = new InvalidOperationException("The actual ordered tail entry is absent.");
                try
                {
                    Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        snapshot = state.Snapshot?.Index ?? 0L,
                        commit = state.CommittedIndex,
                        last = state.LastIndex,
                        tail = tailPosition,
                        hasEntry = false
                    }));
                }
                catch (Exception diagnostic)
                { throw new AggregateException(failure, diagnostic); }
                throw failure;
            }
            return new EmptyReplicaSnapshotCut(state, ReplicaProtocolCodec.Deserialize<ReplicaEntry>(entry));
        });
    }

    internal static void RequireImage(ClusterFixture fixture, string node, ReplicaSnapshot snapshot)
    {
        var expected = snapshot.TransferId.ToString(ClusterFixtureProtocol.GuidFormat) + ReplicaProtocol.SnapshotExtension;
        if (snapshot.FileName != expected)
        { throw new InvalidOperationException("The native snapshot filename does not match its transfer identity."); }
        using var image = NodeEpochRf3OfflineFiles.OpenRead(Path.Combine(RequireRoot(fixture, node),
            ReplicaProtocol.SnapshotDirectory, expected));
        if (image.Length != snapshot.Length || Convert.ToHexStringLower(SHA256.HashData(image)) != snapshot.Sha256)
        { throw new InvalidOperationException("The installed native snapshot image does not match its complete checksum."); }
    }

    private static string RequireRoot(ClusterFixture fixture, string node)
    {
        if (!ClusterFixtureProtocol.IsNodeName(node))
        { throw new ArgumentOutOfRangeException(nameof(node)); }
        var parent = Path.GetFullPath(fixture.Root);
        var root = Path.GetFullPath(Path.Combine(parent, node));
        if (Path.GetDirectoryName(root) != parent || !Directory.Exists(root))
        { throw new InvalidOperationException("The actual fixture-owned follower root is absent."); }
        RequireDirectory(parent);
        RequireDirectory(root);
        return root;
    }

    private static void RequireTree(string path)
    {
        RequireDirectory(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            { throw new InvalidOperationException("The owned follower storage contains a link."); }
            if (Directory.Exists(entry))
            { RequireTree(entry); }
            else
            { NodeEpochRf3OfflineFiles.RequireRegular(entry); NodeEpochRf3OfflineFiles.AssertExclusive(entry); }
        }
    }

    private static void RequireDirectory(string path)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidOperationException("The owned follower storage is not a regular directory."); }
    }
}

internal sealed record EmptyReplicaSnapshotCut(ReplicaHardState State, ReplicaEntry Tail);
