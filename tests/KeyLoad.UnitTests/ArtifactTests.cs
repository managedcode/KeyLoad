using KeyLoad.Artifacts;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

public sealed class ArtifactTests
{
    [Fact]
    public async Task ChunkedCartographBackupRoundTripsAndManagedCodeStorageTransfersIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-artifact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var store = new ZoneTreeStore(new(Path.Combine(root, "source"))))
            {
                store.Commit((tx, _) => { tx.PutRecord(KeyLoad.Storage.KeyCodec.Encode("large"), new string('a', 10_000)); return true; });
                store.Compact();
                store.CreateBackup(Path.Combine(root, "backup"));
            }
            var artifact = Path.Combine(root, "backup.ctg");
            BackupArtifact.Pack(Path.Combine(root, "backup"), artifact, pieceBytes: 1_024);
            var entries = BackupArtifact.Inspect(artifact);
            Assert.Equal(3, entries.Length); Assert.True(entries.Single(e => e.Name == "commands.wal").Pieces > 1);
            BackupArtifact.Unpack(artifact, Path.Combine(root, "unpacked"));
            ZoneTreeStore.Restore(Path.Combine(root, "unpacked"), Path.Combine(root, "restored"));
            using var recovered = new ZoneTreeStore(new(Path.Combine(root, "restored")));
            Assert.Equal(10_000, recovered.Read(view => JsonDefaults.Deserialize<string>(view.Get(KeyLoad.Storage.KeyCodec.Encode("large"))!).Length));
            var copied = await ArtifactTransfer.CopyToFileStorageAsync(artifact, Path.Combine(root, "archive"), TestContext.Current.CancellationToken);
            Assert.True(copied.IsSuccess);
            Assert.Equal(File.ReadAllBytes(artifact), File.ReadAllBytes(Path.Combine(root, "archive", "backup.ctg")));
        }
        finally { Directory.Delete(root, true); }
    }
}
