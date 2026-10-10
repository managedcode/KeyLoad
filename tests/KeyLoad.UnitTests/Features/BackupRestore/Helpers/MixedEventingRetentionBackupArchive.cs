using KeyLoad.Artifacts;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupArchive
{
    internal static async Task RestoreAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        state.CutRows = MixedEventingRetentionBackupOracle.Rows(fixture.Source.Store, state);
        var backup = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Backup);
        var archive = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Archive);
        var unpacked = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Unpacked);
        var capture = MixedEventingCatalogRestore.Capture(fixture.Source, backup, token);
        state.Cut = capture.Position;
        var manifest = await MetadataTestFiles.ReadManifestAsync(backup);
        await Assert.That(manifest.Position).IsEqualTo(state.Cut);
        BackupArtifact.PackCatalogBackup(backup, archive, MixedEventingRetentionBackupProtocol.PieceBytes,
            capture.ManifestDigest, fixture.Source.StorageExecution, fixture.Source.Database.EvaluationClock, token);
        state.ArchiveBytes = await File.ReadAllBytesAsync(archive, token);
        BackupArtifact.UnpackCatalogBackup(archive, unpacked, capture.ManifestDigest, fixture.Source.StorageExecution, token);
        var target = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Target);
        var expectedPosition = await MixedEventingCatalogRestore.RestoreAsync(fixture.Source, capture, unpacked, target, token);
        fixture.OpenTarget(target);
        await Assert.That(fixture.Target!.Position).IsEqualTo(expectedPosition);
        await MixedEventingRetentionBackupOracle.SameAsync(state.CutRows, fixture.Target, state);
        await MixedEventingRetentionBackupOracle.InitialAsync(fixture, state, fixture.Database);
        await SourceAsync(fixture, state, token);
    }

    internal static async Task SourceAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(state.Cut);
        await MixedEventingRetentionBackupOracle.SameAsync(state.CutRows, fixture.Source.Store, state);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Archive), token))
            .IsEquivalentTo(state.ArchiveBytes, CollectionOrdering.Matching);
    }
}
