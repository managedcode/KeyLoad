namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class RestorePublicationRaceFlow
{
    private const string Backup = "publication-original-backup";
    private const string Target = "publication-original-target";

    internal static async Task RunAsync(bool existingEmpty, CancellationToken token)
    {
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var source = EventingArtifactState.FullBytes(fixture.Source.Store);
            var backup = Path.Combine(fixture.Root, Backup);
            var target = Path.Combine(fixture.Root, Target);
            var capture = MixedEventingCatalogRestore.Capture(fixture.Source, backup, token);
            var archive = await CatalogRestoreCancellationFlow.ArchiveAsync(backup, token);
            if (existingEmpty)
            { Directory.CreateDirectory(target); }
            await Assert.That(Directory.Exists(target)).IsEqualTo(existingEmpty);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var authority = new RestorePublicationRaceAuthority(fixture, capture);
                await CompleteAsync(fixture, authority, capture, backup, target, source, archive, token);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }, nativeAdmission: true);
    }

    private static async Task CompleteAsync(EventingArtifactFixture fixture,
        RestorePublicationRaceAuthority authority, NativeCatalogBackupCapture capture,
        string backup, string target, string[] source, IReadOnlyDictionary<string, byte[]> archive,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
            RestorePublicationRaceRefusal.RunAsync(fixture, authority, backup, target, token), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
            CatalogRestoreCancellationFlow.SourceAsync(fixture, capture.Position, source, backup, archive), failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var identity = authority.Restore(fixture, backup, target, null, token);
            await authority.RequireIdentityAsync(identity, fixture.Source.Store.Identity);
            fixture.OpenTarget(target);
            await Assert.That(fixture.Target!.Position).IsEqualTo(authority.ExpectedPosition);
            await EventingArtifactState.AssertSeedAsync(fixture, fixture.Database);
            await EventingArtifactState.SameAsync(
                EventingArtifactState.Bytes(fixture.Source.Store, fixture.Source.Partition),
                fixture.Target, fixture.Source.Partition);
            await CatalogRestoreCancellationCold.RunAsync(fixture, target, authority.ExpectedPosition, token);
        }, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
            CatalogRestoreCancellationFlow.SourceAsync(fixture, capture.Position, source, backup, archive), failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
