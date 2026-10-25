using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class BackupCreateSourceAccessAssertions
{
    private const string ManifestName = "backup.json";
    private const string IdentityName = "identity.json";
    private const string JournalName = "commands.wal";
    private const int SourceRowsWithSentinel = 4;
    private const string MissingName = "The original owned failed backup has an unnamed file.";

    internal static async Task UnpublishedAsync(string candidate)
        => await Assert.That(File.Exists(Path.Combine(candidate, ManifestName))).IsFalse();

    internal static async Task OwnedCutAsync(ZoneTreeStore store,
        (StoreIdentity Identity, long Position, string[] Rows) original, CancellationToken token)
    {
        var current = store.Read(view =>
        {
            token.ThrowIfCancellationRequested();
            var page = view.Scan([], SourceRowsWithSentinel);
            return (store.Identity, store.Position, page.HasMore, Rows: page.Records.Select(row =>
                Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
        });
        await Assert.That(current.HasMore).IsFalse();
        await BackupJournalRepairAssertions.SameCutAsync(original,
            (current.Identity, current.Position, current.Rows));
    }

    internal static async Task ReconcileOwnedPartialAsync(string candidate)
    {
        await UnpublishedAsync(candidate);
        await Assert.That(Directory.EnumerateDirectories(candidate).Any()).IsFalse();
        foreach (var file in Directory.EnumerateFiles(candidate))
        {
            var name = Path.GetFileName(file) ?? throw new InvalidOperationException(MissingName);
            await Assert.That(name is IdentityName or JournalName).IsTrue();
            File.Delete(file);
        }
        await Assert.That(Directory.EnumerateFileSystemEntries(candidate).Any()).IsFalse();
    }

    internal static async Task RestoredAsync(BackupCutFixture fixture, CliBackupRestoreProcessResult result,
        CancellationToken token)
    {
        var current = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, current.Identity);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, current.Identity, current);
    }
}
