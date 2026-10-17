namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogRestoreCanonicalCutFlow
{
    private const string Backup = "canonical-cut-backup";
    private const string Target = "canonical-cut-target";
    private const string StagingPrefix = ".keyload-restore-*";
    private const string InvalidCut = "The catalog backup metadata does not match its original canonical native cut.";

    internal static async Task RunAsync(CancellationToken token)
    {
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var original = EventingArtifactState.FullBytes(fixture.Source.Store);
            var backup = Path.Combine(fixture.Root, Backup);
            var target = Path.Combine(fixture.Root, Target);
            var capture = MixedEventingCatalogRestore.Capture(fixture.Source, backup, token);
            var archive = await CatalogRestoreCancellationFlow.ArchiveAsync(backup, token);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var authority = new CatalogRestoreCancellationAuthority(fixture, capture);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                    () => CompleteAsync(fixture, capture, authority, backup, target,
                        original, archive, token), failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }, nativeAdmission: true);
    }

    private static async Task CompleteAsync(EventingArtifactFixture fixture,
        global::KeyLoad.NativeCatalogBackupCapture capture,
        CatalogRestoreCancellationAuthority authority, string backup, string target,
        string[] original, IReadOnlyDictionary<string, byte[]> archive, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => RejectAsync(fixture, capture, authority, backup, target, original, token), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => CatalogRestoreCanonicalCutArchive.RepairAsync(backup, archive, CancellationToken.None), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => CatalogRestoreCancellationFlow.SourceAsync(fixture, capture.Position,
                original, backup, archive), failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        var identity = authority.Restore(fixture, backup, target,
            fixture.Source.CreateReadWork(token), null, token);
        await authority.RequireIdentityAsync(identity, fixture.Source.Store.Identity);
        fixture.OpenTarget(target);
        await Assert.That(fixture.Target!.Position).IsEqualTo(authority.ExpectedPosition);
        await EventingArtifactState.AssertSeedAsync(fixture, fixture.Database);
        await EventingArtifactState.SameAsync(
            EventingArtifactState.Bytes(fixture.Source.Store, fixture.Source.Partition),
            fixture.Target, fixture.Source.Partition);
        await CatalogRestoreCancellationCold.RunAsync(fixture, target, authority.ExpectedPosition, token);
        await CatalogRestoreCancellationFlow.SourceAsync(fixture, capture.Position, original, backup, archive);
    }

    private static async Task RejectAsync(EventingArtifactFixture fixture,
        global::KeyLoad.NativeCatalogBackupCapture capture,
        CatalogRestoreCancellationAuthority authority, string backup, string target,
        string[] original, CancellationToken token)
    {
        await CatalogRestoreCanonicalCutArchive.ReplaceDigestAsync(backup, token);
        var rejectedArchive = await CatalogRestoreCancellationFlow.ArchiveAsync(backup, token);
        token.ThrowIfCancellationRequested();
        var failure = Assert.ThrowsExactly<global::KeyLoad.KeyLoadException>(() =>
            authority.Restore(fixture, backup, target, fixture.Source.CreateReadWork(token), null, token));
        await Assert.That(failure.Code).IsEqualTo(global::KeyLoad.ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(InvalidCut);
        await Assert.That(failure.InnerException).IsNull();
        await Assert.That(Directory.Exists(target)).IsFalse();
        await Assert.That(Directory.EnumerateDirectories(fixture.Root, StagingPrefix).Any()).IsFalse();
        await CatalogRestoreCancellationFlow.SourceAsync(fixture, capture.Position,
            original, backup, rejectedArchive);
    }
}
