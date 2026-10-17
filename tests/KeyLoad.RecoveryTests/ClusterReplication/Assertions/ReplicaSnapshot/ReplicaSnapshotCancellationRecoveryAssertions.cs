using System.Security.Cryptography;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotCancellationRecoveryAssertions
{
    private const int SingleCancellation = 1;
    private const int FirstOffset = 0;

    internal static async Task CancelAsync(ReplicaCrashNode node, Func<CancellationToken, Task> invoke,
        CancellationToken caller)
    {
        var before = Capture(node);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller);
        await cancellation.CancelAsync();
        Task? original = null;
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() =>
        {
            original = invoke(cancellation.Token);
            return original;
        }, observed);
        var error = observed.FirstOrDefault() as OperationCanceledException;
        var matches = observed.Count == SingleCancellation && original is { IsCanceled: true }
            && error is not null && error.CancellationToken == cancellation.Token;
        var failures = new List<Exception>();
        if (!matches)
        {
            failures.AddRange(observed);
        }
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(matches).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(() => EqualAsync(before, Capture(node)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static ReplicaSnapshotCancellationRecoveryCut Capture(ReplicaCrashNode node)
        => new(Rows(node.Canonical), node.Canonical.Position, Rows(node.ReplicaStore), node.ReplicaStore.Position,
            ReplicaProtocolCodec.Serialize(node.Log.State), Identity(node.Canonical), Identity(node.ReplicaStore),
            node.Snapshots.Current, Files(node));

    internal static async Task EqualAsync(ReplicaSnapshotCancellationRecoveryCut expected,
        ReplicaSnapshotCancellationRecoveryCut actual)
    {
        await Assert.That(actual.Canonical.SequenceEqual(expected.Canonical, StringComparer.Ordinal)).IsTrue();
        await Assert.That(actual.CanonicalPosition).IsEqualTo(expected.CanonicalPosition);
        await Assert.That(actual.Replica.SequenceEqual(expected.Replica, StringComparer.Ordinal)).IsTrue();
        await Assert.That(actual.ReplicaPosition).IsEqualTo(expected.ReplicaPosition);
        await Assert.That(actual.HardState.AsSpan().SequenceEqual(expected.HardState)).IsTrue();
        await ReplicaSnapshotInstallFencingState.IdentityAsync(expected.CanonicalIdentity, actual.CanonicalIdentity);
        await ReplicaSnapshotInstallFencingState.IdentityAsync(expected.ReplicaIdentity, actual.ReplicaIdentity);
        await Assert.That(actual.Published).IsEqualTo(expected.Published);
        await Assert.That(actual.Files.Length).IsEqualTo(expected.Files.Length);
        foreach (var pair in expected.Files.Zip(actual.Files))
        {
            await Assert.That(pair.Second.Name).IsEqualTo(pair.First.Name);
            await Assert.That(pair.Second.Length).IsEqualTo(pair.First.Length);
            await Assert.That(pair.Second.Digest).IsEqualTo(pair.First.Digest);
            await Assert.That(pair.Second.Bytes.AsSpan().SequenceEqual(pair.First.Bytes)).IsTrue();
        }
    }

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

    private static ReplicaSnapshotCancellationRecoveryFile[] Files(ReplicaCrashNode node)
    {
        var directory = Path.Combine(node.Configuration.Directory, ReplicaProtocol.SnapshotDirectory);
        var paths = Directory.EnumerateFiles(directory, ReplicaSnapshotInstallFencingProtocol.FilePattern)
            .Take(ReplicaSnapshotInstallFencingProtocol.MaximumSnapshotFiles + ReplicaSnapshotInstallFencingProtocol.OneByte)
            .Order(StringComparer.Ordinal).ToArray();
        if (paths.Length > ReplicaSnapshotInstallFencingProtocol.MaximumSnapshotFiles)
        {
            throw new InvalidOperationException(ReplicaSnapshotInstallFencingProtocol.InvalidInventory);
        }
        return paths.Select(path => ReadFile(node, path)).ToArray();
    }

    private static ReplicaSnapshotCancellationRecoveryFile ReadFile(ReplicaCrashNode node, string path)
    {
        var name = Path.GetFileName(path);
        using var stream = File.OpenRead(path);
        var maximum = name == ReplicaProtocol.IncomingManifest
            ? CrashExecutionOptions.Replica().Value.MaximumManifestBytes : node.Configuration.MaxSnapshotBytes;
        if (stream.Length > maximum)
        {
            throw new InvalidOperationException(ReplicaSnapshotInstallFencingProtocol.InvalidInventory);
        }
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        stream.Position = FirstOffset;
        return new(name, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)), bytes);
    }
}
