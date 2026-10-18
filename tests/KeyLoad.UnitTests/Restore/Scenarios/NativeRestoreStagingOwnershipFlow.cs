using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class NativeRestoreStagingOwnershipFlow
{
    private const string Backup = "staging-owner-backup";
    private const string Target = "staging-owner-target";

    internal static async Task RunAsync(CancellationToken token)
    {
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var source = EventingArtifactState.FullBytes(fixture.Source.Store);
            var backup = Path.Combine(fixture.Root, Backup);
            var target = Path.Combine(fixture.Root, Target);
            var capture = MixedEventingCatalogRestore.Capture(fixture.Source, backup, token);
            var archive = await CatalogRestoreCancellationFlow.ArchiveAsync(backup, token);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var authority = new RestorePublicationRaceAuthority(fixture, capture);
                var refusal = new NativeRestoreStagingOwnerRefusal(fixture.Root);
                await refusal.RunAsync(fixture, authority, backup, target, token);
                await CatalogRestoreCancellationFlow.SourceAsync(fixture, capture.Position, source, backup, archive);
                await ReopenRetainedAsync(fixture, refusal.Staging, capture.Position);
                var identity = authority.Restore(fixture, backup, target, null, token);
                await authority.RequireIdentityAsync(identity, fixture.Source.Store.Identity);
                fixture.OpenTarget(target);
                await Assert.That(fixture.Target!.Position).IsEqualTo(authority.ExpectedPosition);
                await EventingArtifactState.AssertSeedAsync(fixture, fixture.Database);
                await EventingArtifactState.SameAsync(
                    EventingArtifactState.Bytes(fixture.Source.Store, fixture.Source.Partition),
                    fixture.Target, fixture.Source.Partition);
                await CatalogRestoreCancellationCold.RunAsync(fixture, target, authority.ExpectedPosition, token);
                await CatalogRestoreCancellationFlow.SourceAsync(fixture, capture.Position, source, backup, archive);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        });
    }

    private static async Task ReopenRetainedAsync(EventingArtifactFixture fixture, string staging,
        long originalPosition)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var retained = new ZoneTreeStore(new(staging), fixture.Source.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(retained.Position).IsEqualTo(originalPosition + CatalogReconciliationCommits);
                await EventingArtifactState.SameAsync(
                    EventingArtifactState.Bytes(fixture.Source.Store, fixture.Source.Partition),
                    retained, fixture.Source.Partition);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private const long CatalogReconciliationCommits = 1;
}
