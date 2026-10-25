using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class RestoreUnsafeDestinationFlow
{
    private const string LinkName = "restore-link";
    private const string TargetName = "empty-link-target";
    private const string InvalidTarget = "The execution backup destination is not a regular directory path.";

    internal static async Task RunAsync(bool ancestorLink, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture(checkpoint: true);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                var root = Path.GetDirectoryName(fixture.Source)!;
                var link = Path.Combine(root, LinkName);
                var target = Path.Combine(root, TargetName);
                Directory.CreateDirectory(target);
                Directory.CreateSymbolicLink(link, target);
                var destination = ancestorLink ? Path.Combine(link, MetadataTestContract.RestoredDirectoryName) : link;
                var archive = await ArchiveAsync(fixture.Backup);
                var source = await SourceAsync(fixture, token);
                await RefuseAsync(fixture, destination);
                await Assert.That(new DirectoryInfo(link).LinkTarget).IsEqualTo(target);
                await Assert.That(Directory.EnumerateFileSystemEntries(target).Any()).IsFalse();
                await Assert.That(await SourceAsync(fixture, token)).IsEquivalentTo(source, CollectionOrdering.Matching);
                await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(fixture.Backup, archive);
                Directory.Delete(link);
                var identity = ZoneTreeStore.Restore(fixture.Backup, destination, UnitExecutionOptions.StorageExecution());
                await RestoreUnsafeDestinationCold.RunAsync(fixture, destination, identity, token);
                await Assert.That(Directory.EnumerateFileSystemEntries(target).Any()).IsFalse();
                await Assert.That(await SourceAsync(fixture, token)).IsEquivalentTo(source, CollectionOrdering.Matching);
                await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(fixture.Backup, archive);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefuseAsync(BackupCutFixture fixture, string destination)
    {
        KeyLoadException? original = null;
        try
        {
            ZoneTreeStore.Restore(fixture.Backup, destination, UnitExecutionOptions.StorageExecution());
        }
        catch (KeyLoadException failure) { original = failure; }
        await Assert.That(original).IsNotNull();
        await Assert.That(original!.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(original.Message).IsEqualTo(InvalidTarget);
        var root = Path.GetDirectoryName(fixture.Source)!;
        await Assert.That(Directory.EnumerateDirectories(root, ".keyload-restore-*").Any()).IsFalse();
    }

    private static async Task<string[]> SourceAsync(BackupCutFixture fixture, CancellationToken token)
    {
        string[]? source = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            using var store = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(store.Position).IsEqualTo(fixture.Position);
                source = QueueWholeFlowStorage.Bytes(store);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return source ?? throw new InvalidOperationException("The complete source image was not observed.");
    }

    private static async Task<Dictionary<string, byte[]>> ArchiveAsync(string backup)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(backup, file), await File.ReadAllBytesAsync(file));
        }
        return result;
    }
}
