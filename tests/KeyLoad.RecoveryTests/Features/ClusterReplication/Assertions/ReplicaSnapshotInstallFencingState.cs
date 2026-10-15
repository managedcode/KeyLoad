using System.Security.Cryptography;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotInstallFencingState
{
    internal static ReplicaSnapshotInstallFencingCut Capture(ReplicaCrashNode node)
        => new(Rows(node.Canonical), node.Canonical.Position, Rows(node.ReplicaStore), node.ReplicaStore.Position,
            ReplicaProtocolCodec.Serialize(node.Log.State), Identity(node.Canonical), Identity(node.ReplicaStore),
            node.Snapshots.Current, Files(node));

    internal static async Task EqualAsync(ReplicaSnapshotInstallFencingCut expected, ReplicaSnapshotInstallFencingCut actual)
    {
        await Assert.That(actual.Canonical.SequenceEqual(expected.Canonical, StringComparer.Ordinal)).IsTrue();
        await Assert.That(actual.CanonicalPosition).IsEqualTo(expected.CanonicalPosition);
        await Assert.That(actual.Replica.SequenceEqual(expected.Replica, StringComparer.Ordinal)).IsTrue();
        await Assert.That(actual.ReplicaPosition).IsEqualTo(expected.ReplicaPosition);
        await Assert.That(actual.HardState.AsSpan().SequenceEqual(expected.HardState)).IsTrue();
        await IdentityAsync(expected.CanonicalIdentity, actual.CanonicalIdentity);
        await IdentityAsync(expected.ReplicaIdentity, actual.ReplicaIdentity);
        await Assert.That(actual.Published).IsEqualTo(expected.Published);
        await Assert.That(actual.Files.Length).IsEqualTo(expected.Files.Length);
        foreach (var pair in expected.Files.Zip(actual.Files))
        {
            await Assert.That(pair.First.Name == pair.Second.Name && pair.First.Length == pair.Second.Length
                && pair.First.Digest == pair.Second.Digest).IsTrue();
            await Assert.That((pair.First.PrivateBytes ?? []).AsSpan()
                .SequenceEqual(pair.Second.PrivateBytes ?? [])).IsTrue();
        }
    }

    internal static async Task PrefixAsync(ReplicaCrashNode node, ReplicaSnapshot image, ReplicaProcessTrial trial)
    {
        await ReplicaPrefixGcReceiptAssertions.DocumentAsync(node, ReplicaSnapshotInstallFencingProtocol.OriginalCut);
        await Assert.That(node.Log.State.CommittedIndex).IsEqualTo((long)ReplicaSnapshotInstallFencingProtocol.OriginalCut);
        await Assert.That(node.Log.State.LastIndex).IsEqualTo((long)ReplicaSnapshotInstallFencingProtocol.TailCut);
        await Assert.That(node.Canonical.Identity.NodeId).IsEqualTo(trial.Ready.NodeId);
        await Assert.That(node.Canonical.Identity.ReadGeneration).IsEqualTo(ReplicaSnapshotInstallFencingProtocol.OriginalGeneration);
        await Assert.That(node.HasObsoleteRecord()).IsTrue();
        await Assert.That(node.Snapshots.Current).IsNull();
        var files = Files(node);
        var manifest = files.Single(value => value.Name == ReplicaProtocol.IncomingManifest);
        await Assert.That(manifest.PrivateBytes!.AsSpan().SequenceEqual(ReplicaProtocolCodec.Serialize(image))).IsTrue();
        var prefix = files.Single(value => value.Name == ReplicaProtocol.IncomingImage);
        await Assert.That(prefix.Length).IsEqualTo((long)ReplicaCrashTransfer.PrefixBytes);
        await Assert.That(image.Length).IsGreaterThan(prefix.Length + ReplicaSnapshotInstallFencingProtocol.OneByte);
    }

    internal static async Task IdentityAsync(StoreIdentity expected, StoreIdentity actual)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    private static StoreIdentity Identity(ZoneTreeStore store)
        => store.Identity with
        {
            SigningKey = store.Identity.SigningKey.ToArray()
        };

    private static string[] Rows(ZoneTreeStore store)
    {
        var page = store.Read(view => view.Scan([], ReplicaSnapshotInstallFencingProtocol.MaximumRows));
        if (page.HasMore)
        {
            throw new InvalidOperationException(ReplicaSnapshotInstallFencingProtocol.InvalidInventory);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span)
            + ReplicaSnapshotInstallFencingProtocol.RowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();
    }

    private static ReplicaSnapshotInstallFencingFile[] Files(ReplicaCrashNode node)
    {
        var directory = Path.Combine(node.Configuration.Directory, ReplicaProtocol.SnapshotDirectory);
        var paths = Directory.EnumerateFiles(directory, ReplicaSnapshotInstallFencingProtocol.FilePattern)
            .Take(ReplicaSnapshotInstallFencingProtocol.MaximumSnapshotFiles + ReplicaSnapshotInstallFencingProtocol.OneByte)
            .Order(StringComparer.Ordinal).ToArray();
        if (paths.Length > ReplicaSnapshotInstallFencingProtocol.MaximumSnapshotFiles)
        {
            throw new InvalidOperationException(ReplicaSnapshotInstallFencingProtocol.InvalidInventory);
        }
        return paths.Select(path => File(node, path)).ToArray();
    }

    private static ReplicaSnapshotInstallFencingFile File(ReplicaCrashNode node, string path)
    {
        var name = Path.GetFileName(path);
        using var stream = System.IO.File.OpenRead(path);
        var maximum = name == ReplicaProtocol.IncomingManifest
            ? CrashExecutionOptions.Replica().Value.MaximumManifestBytes : node.Configuration.MaxSnapshotBytes;
        if (stream.Length > maximum)
        {
            throw new InvalidOperationException(ReplicaSnapshotInstallFencingProtocol.InvalidInventory);
        }
        byte[]? bytes = null;
        if (name == ReplicaProtocol.IncomingImage || name == ReplicaProtocol.IncomingManifest)
        {
            if (name == ReplicaProtocol.IncomingImage && stream.Length != ReplicaCrashTransfer.PrefixBytes)
            {
                throw new InvalidOperationException(ReplicaSnapshotInstallFencingProtocol.InvalidInventory);
            }
            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            stream.Position = ReplicaSnapshotInstallFencingProtocol.FirstOffset;
        }
        return new(name, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)), bytes);
    }
}
