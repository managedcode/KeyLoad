using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree.IO;
using KeyLoad.UnitTests.OrleansExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class MetadataEntryOpenFlow
{
    internal static async Task RunAsync(Func<MetadataEntryOpenOwner, Task> operation)
    {
        var failures = new List<Exception>();
        MetadataEntryOpenOwner? owner = null;
        ServerFailureObserver.Observe(() => owner = new(), failures);
        if (owner is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => operation(owner), failures);
            owner.Cleanup(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task FifoAsync(MetadataEntryOpenOwner owner)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }
        var files = owner.Files;
        var original = await files.CaptureStoreEntriesAsync();
        var path = files.HideFile(ZoneTreeExistingStoreFixture.IdentityFile);
        RequestCqrsProbeFifo.Create(path);
        var entry = OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectPath(path) : OfflineLinuxFileSystem.InspectPath(path);
        var entries = Directory.GetFileSystemEntries(files.DirectoryPath);
        var mode = ReadUnixMode(path);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RefuseAsync(owner), failures);
        if (!owner.CanAdvance)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var retained = OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectPath(path) : OfflineLinuxFileSystem.InspectPath(path);
            await Assert.That(retained).IsEqualTo(entry);
            await Assert.That(Directory.GetFileSystemEntries(files.DirectoryPath)).IsEquivalentTo(entries);
            await Assert.That(ReadUnixMode(path)).IsEqualTo(mode);
        }, failures);
        ServerFailureObserver.Observe(() => File.Delete(path), failures);
        await RestoreAsync(owner, path, original, failures);
    }

    internal static async Task SymlinkAsync(MetadataEntryOpenOwner owner)
    {
        var files = owner.Files;
        var original = await files.CaptureStoreEntriesAsync();
        var bytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var path = files.HideFile(ZoneTreeExistingStoreFixture.IdentityFile);
        File.CreateSymbolicLink(path, files.HiddenIdentityPath);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RefuseAsync(owner), failures);
        if (!owner.CanAdvance)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(new FileInfo(path).LinkTarget).IsEqualTo(files.HiddenIdentityPath);
            await Assert.That(await File.ReadAllBytesAsync(files.HiddenIdentityPath))
                .IsEquivalentTo(bytes, CollectionOrdering.Matching);
        }, failures);
        ServerFailureObserver.Observe(() => File.Delete(path), failures);
        await RestoreAsync(owner, path, original, failures);
    }

    private static async Task RefuseAsync(MetadataEntryOpenOwner owner)
    {
        var result = await owner.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result,
            ExistingStoreInspectionExpectedFailures.FormatUnsupported, ExistingStoreInspectionExpectedFailures.KeyLoad);
    }

    private static async Task RestoreAsync(MetadataEntryOpenOwner owner, string path,
        IReadOnlyDictionary<string, byte[]?> original, List<Exception> failures)
    {
        var files = owner.Files;
        ServerFailureObserver.Observe(() => File.Move(files.HiddenIdentityPath, path), failures);
        await ServerFailureObserver.ObserveAsync(() => files.AssertStoreEntriesUnchangedAsync(original), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataEntryOpenHealthy.ReopenAsync(owner), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static UnixFileMode ReadUnixMode(string path)
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException();
        }
        return File.GetUnixFileMode(path);
    }
}
