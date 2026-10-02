using KeyLoad.Artifacts;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class ArtifactTests
{
    [Test]
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
            await Assert.That(entries.Length).IsEqualTo(3);
            await Assert.That(entries.Single(e => e.Name == "commands.wal").Pieces > 1).IsTrue();
            BackupArtifact.Unpack(artifact, Path.Combine(root, "unpacked"));
            ZoneTreeStore.Restore(Path.Combine(root, "unpacked"), Path.Combine(root, "restored"));
            using var recovered = new ZoneTreeStore(new(Path.Combine(root, "restored")));
            await Assert.That(recovered.Read(view => JsonDefaults.Deserialize<string>(view.ReadOwnedValue(KeyLoad.Storage.KeyCodec.Encode("large"))!).Length)).IsEqualTo(10_000);
            var copied = await ArtifactTransfer.CopyToFileStorageAsync(artifact, Path.Combine(root, "archive"), TestContext.Current!.Execution.CancellationToken);
            await Assert.That(copied.IsSuccess).IsTrue();
            var copiedBytes = await File.ReadAllBytesAsync(Path.Combine(root, "archive", "backup.ctg"));
            var artifactBytes = await File.ReadAllBytesAsync(artifact);
            await Assert.That(copiedBytes).IsEquivalentTo(artifactBytes, CollectionOrdering.Matching);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task AcBackup001PackRejectsMissingCanonicalFileBeforePublishingArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-artifact-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "backup.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "commands.wal"), string.Empty);
            var artifact = Path.Combine(root, "missing.ctg");
            var error = Assert.ThrowsExactly<KeyLoadException>(() => BackupArtifact.Pack(root, artifact));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(File.Exists(artifact)).IsFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task AcBackup002NonemptyDestinationRejectsBeforeArchiveIsOpened()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-artifact-destination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "nonempty");
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination, "existing.txt"), "preserved");
            var missingArchive = Path.Combine(root, "missing.ctg");
            var error = Assert.ThrowsExactly<KeyLoadException>(() => BackupArtifact.Unpack(missingArchive, destination));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Conflict);
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, "existing.txt"))).IsEqualTo("preserved");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
