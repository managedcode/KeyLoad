using KeyLoad.Storage.IO;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class NativeRestoreStagingOwnerRefusal(string root)
{
    private const string StagingPattern = ".keyload-restore-*";
    private const string WithheldLock = "original-owned-lock";
    private const string InvalidEntry = "Offline input is not a supported regular file.";
    private const string Unselected = "The native staging owner was not selected.";
    private OfflineFileMetadata originalFifo;

    internal string Staging
    {
        get => field ?? throw new InvalidOperationException(Unselected);
        private set;
    }

    internal async Task RunAsync(EventingArtifactFixture fixture, RestorePublicationRaceAuthority authority,
        string backup, string target, CancellationToken token)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            authority.Restore(fixture, backup, target, ReplaceNativeOwnerPath, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(failure.Message).IsEqualTo(InvalidEntry);
        await Assert.That(failure.StatusCode).IsEqualTo((int)global::System.Net.HttpStatusCode.BadRequest);
        await Assert.That(failure.InnerException).IsNull();
        await Assert.That(Directory.Exists(target)).IsFalse();
        await Assert.That(Directory.EnumerateDirectories(root, StagingPattern)).IsEquivalentTo(new[] { Staging });
        await Assert.That(File.Exists(Path.Combine(Staging, WithheldLock))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(Staging, ZoneTreePersistenceFormat.IdentityFileName))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(Staging, ZoneTreePersistenceFormat.JournalFileName))).IsTrue();
        var ownerPath = Path.Combine(Staging, ZoneTreePersistenceFormat.OwnerLockFileName);
        await Assert.That(Inspect(ownerPath)).IsEqualTo(originalFifo);
        File.Delete(ownerPath);
        File.Move(Path.Combine(Staging, WithheldLock), ownerPath);
    }

    private static OfflineFileMetadata Inspect(string path) => OperatingSystem.IsMacOS()
        ? OfflineMacFileSystem.InspectPath(path) : OfflineLinuxFileSystem.InspectPath(path);

    private void ReplaceNativeOwnerPath()
    {
        Staging = Directory.EnumerateDirectories(root, StagingPattern).Single();
        var ownerPath = Path.Combine(Staging, ZoneTreePersistenceFormat.OwnerLockFileName);
        File.Move(ownerPath, Path.Combine(Staging, WithheldLock));
        RequestCqrsProbeNativeFifo.Create(ownerPath);
        originalFifo = Inspect(ownerPath);
    }
}
