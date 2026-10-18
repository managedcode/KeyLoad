using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupUnsafeDestinationTrial
{
    private const string LinkName = "backup-create-link";
    private const string EmptyTargetName = "backup-create-empty";
    private const string ChildName = "archive";
    private const string InvalidTarget = "The native catalog backup destination is not clean.";

    internal static async Task RunAsync(NativeBackupCutFixture fixture, bool ancestorLink,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var root = Directory.GetParent(fixture.Source)!.FullName;
        var link = Path.Combine(root, LinkName);
        var target = Path.Combine(root, EmptyTargetName);
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        var destination = ancestorLink ? Path.Combine(link, ChildName) : link;
        var archive = Capture(fixture.Backup);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(
                () => OwnedAsync(fixture, source, link, target, destination, archive, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OwnedAsync(NativeBackupCutFixture fixture, ZoneTreeStore source,
        string link, string target, string destination, string[] archive, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(source);
        var position = source.Position;
        var identity = JsonSerializer.Serialize(source.Identity, JsonDefaults.Options);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => source.CreateBackup(destination));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(failure.Message).IsEqualTo(InvalidTarget);
        await Assert.That(new DirectoryInfo(link).LinkTarget).IsEqualTo(target);
        await Assert.That(Directory.EnumerateFileSystemEntries(target).Any()).IsFalse();
        await RequireSourceAsync(source, position, identity, before);
        await Assert.That(Capture(fixture.Backup)).IsEquivalentTo(archive, CollectionOrdering.Matching);
        Directory.Delete(link);
        token.ThrowIfCancellationRequested();
        await Assert.That(source.CreateBackup(destination, token)).IsEqualTo(fixture.Position);
        await RequireSourceAsync(source, position, identity, before);
        var repaired = Capture(destination);
        var restored = ZoneTreeStore.Restore(destination, fixture.Destination, UnitExecutionOptions.StorageExecution(), cancellationToken: token);
        await RestoreUnsafeDestinationCold.RunAsync(fixture, fixture.Destination, restored, token);
        await RequireSourceAsync(source, position, identity, before);
        await Assert.That(Capture(destination)).IsEquivalentTo(repaired, CollectionOrdering.Matching);
        await Assert.That(Capture(fixture.Backup)).IsEquivalentTo(archive, CollectionOrdering.Matching);
        await Assert.That(Directory.EnumerateFileSystemEntries(target).Any()).IsFalse();
    }

    private static async Task RequireSourceAsync(ZoneTreeStore source, long position, string identity,
        string[] before)
    {
        await Assert.That(source.Position).IsEqualTo(position);
        await Assert.That(JsonSerializer.Serialize(source.Identity, JsonDefaults.Options)).IsEqualTo(identity);
        await Assert.That(QueueWholeFlowStorage.Bytes(source)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    private static string[] Capture(string directory)
        => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(File.ReadAllBytes(path)))
            .ToArray();
}
