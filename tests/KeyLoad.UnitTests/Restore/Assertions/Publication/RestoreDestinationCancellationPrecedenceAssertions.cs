using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class RestoreDestinationCancellationPrecedenceAssertions
{
    internal static async Task PreservedAsync(BackupCutFixture fixture,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) source,
        Dictionary<string, byte[]> archive, string parent, string[] entries, bool nonempty,
        CancellationToken token)
    {
        await Assert.That(Directory.Exists(fixture.Destination)).IsTrue();
        await Assert.That(File.Exists(fixture.Destination)).IsFalse();
        var actual = Directory.EnumerateFileSystemEntries(fixture.Destination)
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        string[] expected = nonempty ? [RestoreDestinationCancellationPrecedenceTrial.SentinelName] : [];
        await Assert.That(actual).IsEquivalentTo<string?[], string?>(expected, CollectionOrdering.Matching);
        if (nonempty)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Destination,
                RestoreDestinationCancellationPrecedenceTrial.SentinelName), token);
            await Assert.That(bytes.AsSpan().SequenceEqual(new byte[] { 0x4b, 0x4c, 0x10, 0x25 })).IsTrue();
        }
        await Assert.That(Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(entries, CollectionOrdering.Matching);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }
}
