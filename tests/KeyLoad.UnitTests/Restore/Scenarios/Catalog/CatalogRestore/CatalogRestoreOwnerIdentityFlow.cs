namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogRestoreOwnerIdentityFlow
{
    private const string Backup = "owner-identity-backup";
    private const string Target = "owner-identity-target";
    private const string StagingPrefix = ".keyload-restore-*";
    private const string InvalidMapping = "The restore configuration must map every original owner to a distinct new CLUSTER identity.";

    internal static async Task RunAsync(bool reuseIncarnation, CancellationToken token)
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
                        reuseIncarnation, original, archive, token), failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        });
    }

    private static async Task CompleteAsync(EventingArtifactFixture fixture,
        global::KeyLoad.NativeCatalogBackupCapture capture,
        CatalogRestoreCancellationAuthority authority, string backup, string target,
        bool reuseIncarnation, string[] original, IReadOnlyDictionary<string, byte[]> archive,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failure = Assert.ThrowsExactly<global::KeyLoad.KeyLoadException>(() =>
            authority.RestoreRefusingReusedOwner(fixture, backup, target, reuseIncarnation,
                fixture.Source.CreateReadWork(token), token));
        await Assert.That(failure.Code).IsEqualTo(global::KeyLoad.ErrorCode.Validation);
        await Assert.That(failure.Message).IsEqualTo(InvalidMapping);
        await Assert.That(failure.StatusCode).IsEqualTo((int)global::System.Net.HttpStatusCode.BadRequest);
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
