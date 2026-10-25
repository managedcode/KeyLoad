using KeyLoad.Storage.ZoneTree.Restore;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class MixedEventingRetentionBackupArchive
{
    internal static async Task RestoreAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        state.CutRows = MixedEventingRetentionBackupOracle.Rows(fixture.Source.Store, state);
        var backup = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Backup);
        var archive = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Archive);
        var unpacked = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Unpacked);
        var capture = MixedEventingBackupRestore.Capture(fixture.Source, backup, token);
        state.Cut = capture.Position;
        var manifest = await MetadataTestFiles.ReadManifestAsync(backup);
        await Assert.That(manifest.Position).IsEqualTo(state.Cut);
        BackupArtifact.Pack(backup, archive, MixedEventingRetentionBackupProtocol.PieceBytes,
            fixture.Source.Database.EvaluationClock);
        state.ArchiveBytes = await File.ReadAllBytesAsync(archive, token);
        token.ThrowIfCancellationRequested();
        BackupArtifact.Unpack(archive, unpacked);
        var target = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Target);
        var expectedPosition = await MixedEventingBackupRestore.RestoreAsync(fixture.Source, capture, unpacked, target, token);
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
