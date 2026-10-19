namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class VerifiedBackupMixedCutTransfer
{
    private const string Journal = "commands.wal";

    internal static async Task<IReadOnlyDictionary<string, byte[]>> MixAsync(VerifiedBackupCutSnapshot selected,
        VerifiedBackupCutSnapshot donor, CancellationToken token)
    {
        var before = await MetadataTestFiles.ReadManifestAsync(selected.Path);
        await File.WriteAllBytesAsync(Path.Combine(selected.Path, Journal), donor.Archive[Journal], token);
        await MetadataTestFiles.UpdateManifestFileAsync(selected.Path, Journal);
        var after = await MetadataTestFiles.ReadManifestAsync(selected.Path);
        await Assert.That(after.Position).IsEqualTo(before.Position);
        await Assert.That(after.Version).IsEqualTo(before.Version);
        return await RestoreCancellationFlow.ArchiveAsync(selected.Path, token);
    }

    internal static async Task RepairAsync(VerifiedBackupCutSnapshot selected)
    {
        foreach (var file in selected.Archive)
        {
            await File.WriteAllBytesAsync(Path.Combine(selected.Path, file.Key), file.Value, CancellationToken.None);
        }
        await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(selected.Path, selected.Archive);
    }
}
