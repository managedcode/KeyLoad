using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

internal sealed class CheckpointTests
{
    [Test]
    public async Task CompactionPreservesLogicalPositionAndDropsSupersededPayloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-compact-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new ZoneTreeStore(new(root)))
            {
                for (var n = 0; n < 100; n++)
                {
                    store.Commit((tx, _) =>
                    {
                        tx.PutRecord(KeyCodec.Encode("value"), new string('x', 4_096) + n);
                        return true;
                    });
                }
                var before = new FileInfo(Path.Combine(root, "commands.wal")).Length;
                var snapshot = store.Compact();
                await Assert.That(snapshot.Position).IsEqualTo(100);
                await Assert.That(snapshot.RecordCount).IsEqualTo(1);
                await Assert.That(new FileInfo(Path.Combine(root, "commands.wal")).Length < before / 10).IsTrue();
                var next = store.Commit((tx, position) =>
                {
                    tx.PutRecord(KeyCodec.Encode("next"), 101);
                    return position;
                });
                await Assert.That(next).IsEqualTo(101);
            }
            using var reopened = new ZoneTreeStore(new(root));
            await Assert.That(reopened.Position).IsEqualTo(101);
            await Assert.That(reopened.Identity.FormatVersion).IsEqualTo(ZoneTreePersistenceFormat.BinaryJournalIdentityVersion);
            await Assert.That(reopened.Read(view => NativeSerialization.Deserialize<string>(view.ReadOwnedValue(KeyCodec.Encode("value"))!))).EndsWith("99");
            await Assert.That(reopened.Read(view => NativeSerialization.Deserialize<int>(view.ReadOwnedValue(KeyCodec.Encode("next"))!))).IsEqualTo(101);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
    [Test]
    public async Task ReplicaInstallReplacesOldKeysPreservesNodeIdentityAndRejectsStaleScope()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var incarnation = Guid.NewGuid();
        var signing = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var source = new ZoneTreeStore(new(Path.Combine(root, "source")) { Incarnation = incarnation, SigningKey = signing });
            source.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 7L); tx.PutRecord(KeyCodec.Encode("new"), "value"); return true; });
            var snapshot = Path.Combine(root, "snapshot");
            source.CreateSnapshot(snapshot, 7);
            using var target = new ZoneTreeStore(new(Path.Combine(root, "target")) { Incarnation = incarnation, SigningKey = signing });
            target.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("obsolete"), "old"); return true; });
            var identity = target.Identity;
            var installed = target.InstallSnapshot(snapshot, 7);
            await Assert.That(installed.AppliedPosition).IsEqualTo(7);
            await Assert.That(target.Identity.NodeId).IsEqualTo(identity.NodeId);
            await Assert.That(target.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration + 1);
            await Assert.That(target.Read(view => view.ReadOwnedValue(KeyCodec.Encode("obsolete")))).IsNull();
            await Assert.That(target.Read(view => NativeSerialization.Deserialize<string>(view.ReadOwnedValue(KeyCodec.Encode("new"))!))).IsEqualTo("value");
            target.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 8L); return true; });
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => target.InstallSnapshot(snapshot, 7)).Code).IsEqualTo(ErrorCode.OwnershipLost);
            using var outsider = new ZoneTreeStore(new(Path.Combine(root, "outsider")));
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => outsider.InstallSnapshot(snapshot, 7)).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        }
        finally { Directory.Delete(root, true); }
    }
    [Test]
    public async Task IncompleteOrCorruptCheckpointIsRejectedBeforeTouchingLiveState()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-checkpoint-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new ZoneTreeStore(new(Path.Combine(root, "database")));
            store.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("key"), "safe"); return true; });
            var snapshot = Path.Combine(root, "snapshot");
            store.CreateSnapshot(snapshot);
            var bytes = await File.ReadAllBytesAsync(snapshot);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(snapshot, bytes);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => store.InstallSnapshot(snapshot, 0)).Code).IsEqualTo(ErrorCode.Corruption);
            await File.WriteAllBytesAsync(snapshot, bytes[..^80]);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => store.InstallSnapshot(snapshot, 0)).Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(store.Read(view => NativeSerialization.Deserialize<string>(view.ReadOwnedValue(KeyCodec.Encode("key"))!))).IsEqualTo("safe");
            await Assert.That(store.Position).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
