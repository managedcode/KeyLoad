using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class RestoreCancellationFlow
{
    private const string Backup = "cancel-execution-backup";
    private const string Target = "cancel-execution-target";
    private const string Staging = ".keyload-restore-*";

    internal static async Task RunAsync(bool existingEmpty, CancellationToken token)
    {
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var source = EventingArtifactState.FullBytes(fixture.Source.Store);
            var backup = Path.Combine(fixture.Root, Backup);
            var target = Path.Combine(fixture.Root, Target);
            var capture = MixedEventingBackupRestore.Capture(fixture.Source, backup, token);
            var archive = await ArchiveAsync(backup, token);
            if (existingEmpty)
            {
                Directory.CreateDirectory(target);
            }
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            var failure = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeStore.Restore(
                backup, target, fixture.Source.StorageExecution, cancellationToken: cancelled.Token));
            await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
            await Assert.That(Directory.Exists(target)).IsEqualTo(existingEmpty);
            if (existingEmpty)
            {
                await Assert.That(Directory.EnumerateFileSystemEntries(target).Any()).IsFalse();
            }
            await Assert.That(Directory.EnumerateDirectories(fixture.Root, Staging).Any()).IsFalse();
            await SourceAsync(fixture, capture.Position, source, backup, archive);
            var position = await MixedEventingBackupRestore.RestoreAsync(fixture.Source, capture, backup, target, token);
            fixture.OpenTarget(target);
            await Assert.That(fixture.Target!.Position).IsEqualTo(position);
            await EventingArtifactState.AssertSeedAsync(fixture, fixture.Database);
            await EventingArtifactState.SameAsync(EventingArtifactState.Bytes(fixture.Source.Store,
                fixture.Source.Partition), fixture.Target, fixture.Source.Partition);
            await RestoreColdContinuation.RunAsync(fixture, target, position, token);
            await SourceAsync(fixture, capture.Position, source, backup, archive);
        });
    }

    internal static async Task SourceAsync(EventingArtifactFixture fixture, long position,
        string[] source, string backup, IReadOnlyDictionary<string, byte[]> archive)
    {
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Source.Store))
            .IsEquivalentTo(source, CollectionOrdering.Matching);
        await EventingArtifactState.AssertSeedAsync(fixture, fixture.Source.Database);
        var original = OrleansSerialization.Deserialize<StoredOutcome>(
            BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
                fixture.Source.Partition, EventingArtifactFixture.Principal, fixture.Original.CommandId));
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
