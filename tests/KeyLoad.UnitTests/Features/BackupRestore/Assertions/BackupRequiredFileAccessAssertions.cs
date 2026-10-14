using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupRequiredFileAccessAssertions
{
    private const string JournalName = "commands.wal";
    private const string RestoreStagingPattern = ".keyload-restore-*";
    private const string MissingParent = "The owned backup fixture has no parent directory.";
    private const string MissingName = "The owned backup input has no file name.";

    internal static async Task RefusedCliAsync(CliBackupRestoreProcessResult result)
    {
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.StandardOutput).IsEqualTo(string.Empty);
    }

    internal static async Task NoPublicationAsync(string backup, string destination, bool existingEmpty)
    {
        await Assert.That(File.Exists(destination)).IsFalse();
        await Assert.That(Directory.Exists(destination)).IsEqualTo(existingEmpty);
        if (existingEmpty)
        {
            await Assert.That(Directory.EnumerateFileSystemEntries(destination).Any()).IsFalse();
        }
        var root = Directory.GetParent(backup) ?? throw new DirectoryNotFoundException(MissingParent);
        await Assert.That(Directory.EnumerateDirectories(root.FullName, RestoreStagingPattern,
            SearchOption.AllDirectories).Any()).IsFalse();
    }

    internal static async Task ImmutableInputsAsync(MetadataBackupFixture fixture,
        Dictionary<string, byte[]> original, (StoreIdentity Identity, long Position, string[] Rows) source,
        CancellationToken token)
    {
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.SourceDirectory, token));
        var files = Directory.EnumerateFiles(fixture.BackupDirectory).Select(path =>
            Path.GetFileName(path) ?? throw new InvalidOperationException(MissingName)).ToArray();
        await Assert.That(files).IsEquivalentTo(original.Keys, CollectionOrdering.Matching);
        foreach (var row in original)
        {
            if (row.Key == JournalName)
            {
                continue;
            }
            var actual = await File.ReadAllBytesAsync(Path.Combine(fixture.BackupDirectory, row.Key), token);
            await Assert.That(actual.AsSpan().SequenceEqual(row.Value)).IsTrue();
        }
    }
}
