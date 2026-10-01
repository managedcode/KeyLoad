using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

public sealed class CheckpointTests
{
    [Fact]
    public void CompactionPreservesLogicalPositionAndDropsSupersededPayloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-compact-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new ZoneTreeStore(new(root)))
            {
                for (var n = 0; n < 100; n++) store.Commit((tx, _) =>
                { tx.PutRecord(KeyCodec.Encode("value"), new string('x', 4_096) + n); return true; });
                var before = new FileInfo(Path.Combine(root, "commands.wal")).Length;
                var snapshot = store.Compact();
                Assert.Equal(100, snapshot.Position); Assert.Equal(1, snapshot.RecordCount);
                Assert.True(new FileInfo(Path.Combine(root, "commands.wal")).Length < before / 10);
                store.Commit((tx, position) => { Assert.Equal(101, position); tx.PutRecord(KeyCodec.Encode("next"), 101); return true; });
            }
            using var reopened = new ZoneTreeStore(new(root));
            Assert.Equal(101, reopened.Position); Assert.Equal(2, reopened.Identity.FormatVersion);
            Assert.EndsWith("99", reopened.Read(view => JsonDefaults.Deserialize<string>(view.Get(KeyCodec.Encode("value"))!)));
            Assert.Equal(101, reopened.Read(view => JsonDefaults.Deserialize<int>(view.Get(KeyCodec.Encode("next"))!)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void ReplicaInstallReplacesOldKeysPreservesNodeIdentityAndRejectsStaleScope()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var incarnation = Guid.NewGuid(); var signing = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var source = new ZoneTreeStore(new(Path.Combine(root, "source")) { Incarnation = incarnation, SigningKey = signing });
            source.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 7L); tx.PutRecord(KeyCodec.Encode("new"), "value"); return true; });
            var snapshot = Path.Combine(root, "snapshot"); source.CreateSnapshot(snapshot, 7);
            using var target = new ZoneTreeStore(new(Path.Combine(root, "target")) { Incarnation = incarnation, SigningKey = signing });
            target.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("obsolete"), "old"); return true; });
            var identity = target.Identity;
            var installed = target.InstallSnapshot(snapshot, 7);
            Assert.Equal(7, installed.AppliedPosition); Assert.Equal(identity.NodeId, target.Identity.NodeId);
            Assert.Equal(identity.ReadGeneration + 1, target.Identity.ReadGeneration);
            Assert.Null(target.Read(view => view.Get(KeyCodec.Encode("obsolete"))));
            Assert.Equal("value", target.Read(view => JsonDefaults.Deserialize<string>(view.Get(KeyCodec.Encode("new"))!)));
            target.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("system", "last-applied"), 8L); return true; });
            Assert.Equal(ErrorCode.OwnershipLost, Assert.Throws<KeyLoadException>(() => target.InstallSnapshot(snapshot, 7)).Code);
            using var outsider = new ZoneTreeStore(new(Path.Combine(root, "outsider")));
            Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => outsider.InstallSnapshot(snapshot, 7)).Code);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void IncompleteOrCorruptCheckpointIsRejectedBeforeTouchingLiveState()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-checkpoint-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new ZoneTreeStore(new(Path.Combine(root, "database")));
            store.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode("key"), "safe"); return true; });
            var snapshot = Path.Combine(root, "snapshot"); store.CreateSnapshot(snapshot);
            var bytes = File.ReadAllBytes(snapshot); bytes[^1] ^= 1; File.WriteAllBytes(snapshot, bytes);
            Assert.Equal(ErrorCode.Corruption, Assert.Throws<KeyLoadException>(() => store.InstallSnapshot(snapshot, 0)).Code);
            File.WriteAllBytes(snapshot, bytes[..^80]);
            Assert.Equal(ErrorCode.Corruption, Assert.Throws<KeyLoadException>(() => store.InstallSnapshot(snapshot, 0)).Code);
            Assert.Equal("safe", store.Read(view => JsonDefaults.Deserialize<string>(view.Get(KeyCodec.Encode("key"))!)));
            Assert.Equal(1, store.Position);
        }
        finally { Directory.Delete(root, true); }
    }
}
