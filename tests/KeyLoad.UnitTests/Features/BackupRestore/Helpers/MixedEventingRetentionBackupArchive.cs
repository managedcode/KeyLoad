using KeyLoad.Artifacts;
using KeyLoad.Storage.ZoneTree;
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
        state.Cut = fixture.Source.Store.CreateBackup(backup);
        var manifest = await MetadataTestFiles.ReadManifestAsync(backup);
        await Assert.That(manifest.Position).IsEqualTo(state.Cut);
        BackupArtifact.Pack(backup, archive, pieceBytes: MixedEventingRetentionBackupProtocol.PieceBytes);
        state.ArchiveBytes = await File.ReadAllBytesAsync(archive, token);
        BackupArtifact.Unpack(archive, unpacked);
        var target = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Target);
        var identity = ZoneTreeStore.Restore(unpacked, target, UnitExecutionOptions.StorageExecution());
        await Assert.That(identity.Incarnation).IsNotEqualTo(fixture.Source.Store.Identity.Incarnation);
        await Assert.That(identity.DispatchPaused).IsTrue();
        fixture.OpenTarget(target);
        await Assert.That(fixture.Target!.Position).IsEqualTo(state.Cut + 1);
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
