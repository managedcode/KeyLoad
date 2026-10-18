using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogRestoreCancellationFlow
{
    private const string Backup = "cancel-catalog-backup";
    private const string Target = "cancel-catalog-target";

    internal static async Task RunAsync(bool existingEmpty, CancellationToken token)
        => await RunAsync(existingEmpty, afterReconcile: false, token);

    internal static async Task RunAsync(bool existingEmpty, bool afterReconcile, CancellationToken token)
    {
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var source = EventingArtifactState.FullBytes(fixture.Source.Store);
            var backup = Path.Combine(fixture.Root, Backup);
            var target = Path.Combine(fixture.Root, Target);
            var capture = MixedEventingCatalogRestore.Capture(fixture.Source, backup, token);
            var archive = await ArchiveAsync(backup, token);
            if (existingEmpty)
            {
                Directory.CreateDirectory(target);
            }
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var authority = new CatalogRestoreCancellationAuthority(fixture, capture);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await CompleteAsync(fixture, capture, authority, backup, target,
                        existingEmpty, afterReconcile, source, archive, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        });
    }

    private static async Task CompleteAsync(EventingArtifactFixture fixture, NativeCatalogBackupCapture capture,
        CatalogRestoreCancellationAuthority authority, string backup, string target,
        bool existingEmpty, bool afterReconcile, string[] source, IReadOnlyDictionary<string, byte[]> archive,
        CancellationToken token)
    {
        await CatalogRestoreCancellationRefusal.RunAsync(fixture, authority, backup, target,
            existingEmpty, afterReconcile, token);
        await SourceAsync(fixture, capture.Position, source, backup, archive);
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
        await SourceAsync(fixture, capture.Position, source, backup, archive);
    }

    internal static async Task SourceAsync(EventingArtifactFixture fixture, long position,
        string[] source, string backup, IReadOnlyDictionary<string, byte[]> archive)
    {
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Source.Store))
            .IsEquivalentTo(source, CollectionOrdering.Matching);
        await EventingArtifactState.AssertSeedAsync(fixture, fixture.Source.Database);
        var original = global::KeyLoad.NativeSerialization.Deserialize<global::KeyLoad.Core.StoredOutcome>(
            BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
                fixture.Source.Partition, EventingArtifactFixture.Principal, fixture.Original.Id));
        await EventingArtifactState.SameResultAsync(original.Result,
            fixture.SubmitIssued(fixture.Source.Database, fixture.Original));
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Source.Store))
            .IsEquivalentTo(source, CollectionOrdering.Matching);
        await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(backup, archive);
    }

    internal static async Task<Dictionary<string, byte[]>> ArchiveAsync(string backup, CancellationToken token)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(backup, file), await File.ReadAllBytesAsync(file, token));
        }
        return result;
    }
}
