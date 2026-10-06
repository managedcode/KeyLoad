using KeyLoad.Artifacts;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupExecutionPolicyTests
{
    private const string PieceBytesEnvironment = "KEYLOAD_BACKUP__PIECEBYTES";
    [Test]
    public async Task ConfiguredArchivePiecesRoundTripAndInvalidPolicyCreatesNoArchive()
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var token = TestContext.Current!.Execution.CancellationToken;
        var payload = new string('x', 4_096);
        var key = KeyCodec.Encode("cli-backup/large-policy-record");
        var root = await CliBackupRestoreFixture.RunAsync(async fixture =>
        {
            SeedLargeRecord(fixture, key, payload);
            var backup = await CliBackupRestoreProcess.RunAsync(options,
                ["backup", fixture.SourceDirectory, fixture.BackupDirectory], token);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(backup);
            var files = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, token);
            var rejected = await CliBackupRestoreProcess.RunAsync(options,
                ["pack-backup", fixture.BackupDirectory, fixture.ArtifactPath], token,
                new Dictionary<string, string> { [PieceBytesEnvironment] = "1023" });
            await CliBackupRestoreAssertions.RejectedProcessAsync(rejected);
            await Assert.That(rejected.StandardError.Contains("The backup archive piece size is invalid.",
                StringComparison.Ordinal)).IsTrue();
            await Assert.That(File.Exists(fixture.ArtifactPath)).IsFalse();
            await CliBackupRestoreAssertions.FilesEqualAsync(files, fixture.BackupDirectory, token);
            await fixture.AssertSeedRemainsAsync();

            var packed = await CliBackupRestoreProcess.RunAsync(options,
                ["pack-backup", fixture.BackupDirectory, fixture.ArtifactPath], token,
                new Dictionary<string, string> { [PieceBytesEnvironment] = "1024" });
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(packed);
            await AssertArchivePiecesAsync(fixture.ArtifactPath, files);
            var unpacked = await CliBackupRestoreProcess.RunAsync(options,
                ["unpack-backup", fixture.ArtifactPath, fixture.UnpackedDirectory], token);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(unpacked);
            await CliBackupRestoreAssertions.FilesEqualAsync(files, fixture.UnpackedDirectory, token);
            var restored = await CliBackupRestoreProcess.RunAsync(options,
                ["restore", fixture.UnpackedDirectory, fixture.RestoredDirectory], token);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(restored);
            await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, restored, fixture.RestoredDirectory);
            using var reopened = new ZoneTreeStore(new(fixture.RestoredDirectory),
                UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var value = reopened.Read(view => NativeSerialization.Deserialize<string>(view.ReadOwnedValue(key)!));
            await Assert.That(value).IsEqualTo(payload);
        }, token);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static void SeedLargeRecord(CliBackupRestoreFixture fixture, byte[] key, string payload)
    {
        using var store = new ZoneTreeStore(new(fixture.SourceDirectory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, payload);
            return true;
        });
    }

    private static async Task AssertArchivePiecesAsync(string artifact, IReadOnlyDictionary<string, byte[]> files)
    {
        var entries = BackupArtifact.Inspect(artifact);
        await Assert.That(entries.Select(entry => entry.Name).SequenceEqual(files.Keys)).IsTrue();
        foreach (var entry in entries)
        {
            var length = files[entry.Name].LongLength;
            await Assert.That(entry.Bytes).IsEqualTo(length);
            await Assert.That(entry.Pieces).IsEqualTo(checked((int)Math.Max(1, (length + 1_023) / 1_024)));
        }
        await Assert.That(entries.Any(entry => entry.Pieces > 1)).IsTrue();
    }
}
