namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class VerifiedBackupMixedCutConservation
{
    internal static async Task RunAsync(EventingArtifactFixture fixture, long sourcePosition,
        string[] sourceRows, VerifiedBackupCutSnapshot selected, VerifiedBackupCutSnapshot donor,
        IReadOnlyDictionary<string, byte[]> selectedBytes)
    {
        await CatalogRestoreCancellationFlow.SourceAsync(fixture, sourcePosition, sourceRows,
            donor.Path, donor.Archive);
        await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(selected.Path, selectedBytes);
    }
}
