using KeyLoad.Storage;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class PackedBackupCutConservation
{
    internal static async Task RunAsync(CliBackupRestoreFixture fixture,
        (StoreIdentity Identity, long Position, string[] Rows) source, string selected, Dictionary<string, byte[]> files,
        string donor, Dictionary<string, byte[]> donorFiles, string? artifact, byte[]? artifactBytes, CancellationToken token)
    {
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(files, selected, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(donorFiles, donor, token);
        if (artifact is not null && artifactBytes is not null)
        {
            await Assert.That((await File.ReadAllBytesAsync(artifact, token)).AsSpan().SequenceEqual(artifactBytes)).IsTrue();
        }
    }
}
