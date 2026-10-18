namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogRestoreCredentialFlow
{
    private const string Backup = "operator-credential-backup";
    private const string Target = "operator-credential-target";
    private const string StagingPrefix = ".keyload-restore-*";
    private const string UnavailableCredential = "The credential is unavailable or expired.";

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
                    () => CompleteAsync(fixture, capture, authority, backup, target, original, archive, token), failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        });
    }

    private static async Task CompleteAsync(EventingArtifactFixture fixture,
        global::KeyLoad.NativeCatalogBackupCapture capture, CatalogRestoreCancellationAuthority authority,
        string backup, string target, string[] original, IReadOnlyDictionary<string, byte[]> archive,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failure = Assert.ThrowsExactly<global::KeyLoad.KeyLoadException>(() =>
            authority.RestoreRefusingWrongCredential(fixture, backup, target,
                fixture.Source.CreateReadWork(token), token));
        await Assert.That(failure.Code).IsEqualTo(global::KeyLoad.ErrorCode.Unauthenticated);
        await Assert.That(failure.Message).IsEqualTo(UnavailableCredential);
        await Assert.That(failure.StatusCode).IsEqualTo((int)global::System.Net.HttpStatusCode.Unauthorized);
        await Assert.That(failure.InnerException).IsNull();
        await Assert.That(Directory.Exists(target)).IsFalse();
        await Assert.That(Directory.EnumerateDirectories(fixture.Root, StagingPrefix).Any()).IsFalse();
        await CatalogRestoreCancellationFlow.SourceAsync(fixture, capture.Position, original, backup, archive);
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
}
