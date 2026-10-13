using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogArchiveRetryFlow
{
    private const string Backup = "retained-catalog";
    private const string Target = "retained-target";

    internal static async Task RunAsync(CancellationToken token)
    {
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var backup = Path.Combine(fixture.Root, Backup);
            var target = Path.Combine(fixture.Root, Target);
            var original = Capture(fixture, backup, token);
            await Assert.That(original.Position).IsEqualTo(fixture.Source.Store.Position);
            var domains = EventingArtifactState.Bytes(fixture.Source.Store, fixture.Source.Partition);
            var outcome = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
                fixture.Source.Partition, EventingArtifactFixture.Principal, fixture.Original.Id);
            var files = await CatalogArchiveRetryAssertions.FilesAsync(backup, token);
            var operation = await CatalogArchiveRetryAssertions.AdvanceAsync(fixture, token);
            var image = EventingArtifactState.FullBytes(fixture.Source.Store);
            var position = fixture.Source.Store.Position;
            await Assert.That(position).IsGreaterThan(original.Position);
            await CatalogArchiveRetryAssertions.CaptureAsync(original, Capture(fixture, backup, token));
            await CatalogArchiveRetryAssertions.RefuseAndRepairAsync(fixture, backup, original, token);
            await CatalogArchiveRetryAssertions.CaptureAsync(original, Capture(fixture, backup, token));
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var authority = new CatalogRestoreCancellationAuthority(fixture, original);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var identity = authority.Restore(fixture, backup, target,
                        fixture.Source.CreateReadWork(token), null, token);
                    await authority.RequireIdentityAsync(identity, fixture.Source.Store.Identity);
                    fixture.OpenTarget(target);
                    await CatalogArchiveRetryCold.RunAsync(fixture, target, authority.ExpectedPosition,
                        domains, outcome, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            await CatalogArchiveRetryAssertions.ReplayAsync(fixture, operation, image, position);
            await Assert.That(await CatalogArchiveRetryAssertions.FilesAsync(backup, token))
                .IsEquivalentTo(files, CollectionOrdering.Matching);
        }, nativeAdmission: true).ConfigureAwait(false);
    }

    internal static NativeCatalogBackupCapture Capture(EventingArtifactFixture fixture,
        string backup, CancellationToken token)
    {
        var source = fixture.Source;
        var work = source.CreateReadWork(token);
        var id = Guid.NewGuid();
        NativeCatalogBackupCapture? observed = null;
        var result = source.Store.CaptureOrReadCatalogBackup(backup,
            view => source.Database.CaptureClusterBackupOwner(view, EventingArtifactFixture.Principal,
                id, source.Store.Position, source.Store.Identity, work),
            (view, position, identity) => NativeSerialization.Serialize(source.Database.CaptureClusterBackupOwner(
                view, EventingArtifactFixture.Principal, id, position, identity, work)),
            capture => observed = capture, token);
        if (observed is null)
        { throw new InvalidOperationException("The native catalog capture callback was not joined."); }
        return result;
    }
}
