using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class PackedBackupNativeCutConservation
{
    internal static async Task RunAsync(CliBackupRestoreFixture fixture,
        (StoreIdentity Identity, long Position, string[] Rows) source, string selected, Dictionary<string, byte[]> files,
        string donor, Dictionary<string, byte[]> donorFiles, string? artifact, byte[]? artifactBytes, CancellationToken token)
    {
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.SourceDirectory, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(files, selected, token);
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(donorFiles, donor, token);
        if (artifact is not null && artifactBytes is not null)
        { await Assert.That((await File.ReadAllBytesAsync(artifact, token)).AsSpan().SequenceEqual(artifactBytes)).IsTrue(); }
    }
}
