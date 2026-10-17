using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupCreateSourceAccessAssertions
{
    private const string ManifestName = "backup.json";
    private const string IdentityName = "identity.json";
    private const string JournalName = "commands.wal";
    private const int SourceRowsWithSentinel = 4;
    private const long RestoreAuthorityCommits = 1L;
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
        await NativeBackupJournalRepairAssertions.SameCutAsync(original,
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

    internal static async Task RestoredAsync(NativeBackupCutFixture fixture, CliBackupRestoreProcessResult result,
        CancellationToken token)
    {
        var current = NativeBackupJournalRepairAssertions.Read(fixture.Destination, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, current.Identity);
        await Assert.That(current.Identity.NodeId).IsNotEqualTo(fixture.Identity.NodeId);
        await Assert.That(current.Identity.Incarnation).IsNotEqualTo(fixture.Identity.Incarnation);
        await Assert.That(current.Identity.SigningKey.Span.SequenceEqual(fixture.Identity.SigningKey.Span)).IsFalse();
        await Assert.That(current.Identity.Durability).IsEqualTo(fixture.Identity.Durability);
        await Assert.That(current.Identity.DispatchPaused).IsTrue();
        await Assert.That(current.Position).IsEqualTo(fixture.Position + RestoreAuthorityCommits);
        string[] expected =
        [
            Convert.ToHexString(MetadataBackupFixture.StoredKeyBytes) + ":" +
                Convert.ToHexString(global::KeyLoad.NativeSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.DispatchPausedKey)) + ":" +
                Convert.ToHexString(global::KeyLoad.NativeSerialization.Serialize(true))
        ];
        await Assert.That(current.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
    }
}
