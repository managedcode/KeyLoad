namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class MixedEventingRetentionBackupArchive
{
    internal static async Task RestoreAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        state.CutRows = MixedEventingRetentionBackupOracle.Rows(fixture.Source.Store, state);
        var backup = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Backup);
        var capture = await MixedEventingBackupRestore.CaptureAsync(fixture.Source, backup, token);
        state.Cut = capture.Position;
        state.BackupFiles = await RestoreCancellationFlow.ArchiveAsync(backup, token);
        token.ThrowIfCancellationRequested();
        var target = Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Target);
        await MixedEventingBackupRestore.RestoreAsync(fixture.Source, capture, backup, target, token);
        fixture.OpenTarget(target);
        await Assert.That(fixture.Target!.Position).IsGreaterThan(0);
        await MixedEventingRetentionBackupOracle.SameAsync(state.CutRows, fixture.Target, state);
        await MixedEventingRetentionBackupOracle.InitialAsync(fixture, state, fixture.Database);
        await SourceAsync(fixture, state);
    }

    internal static async Task SourceAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state)
    {
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(state.Cut);
        await MixedEventingRetentionBackupOracle.SameAsync(state.CutRows, fixture.Source.Store, state);
        await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(
            Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Backup), state.BackupFiles);
    }
}
