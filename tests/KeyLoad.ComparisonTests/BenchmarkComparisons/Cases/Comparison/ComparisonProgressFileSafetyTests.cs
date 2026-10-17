namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-LIVE-002/003: diagnostic collisions and links never modify an unrelated file.</summary>
internal sealed class ComparisonProgressFileSafetyTests
{
    private const string Marker = "KeyLoadBenchmarkProgress phase=measure repetition=1 completed=1 total=10 failed=0 elapsedSeconds=1";
    private const string ProtectedBytes = "unrelated-existing-bytes";
    private const string PendingSuffix = ".pending";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PendingCollisionPreservesExistingFileOrSymbolicLink(bool symbolicLink)
    {
        var root = ComparisonProgressFileTests.CreateRoot("keyload-progress-collision-");
        var path = Path.Combine(root.FullName, ComparisonProgressLine.FileName);
        var pending = path + PendingSuffix;
        var protectedFile = Path.Combine(root.FullName, "outside.log");
        await File.WriteAllTextAsync(protectedFile, ProtectedBytes);
        if (symbolicLink)
        {
            File.CreateSymbolicLink(pending, protectedFile);
        }
        else
        {
            await File.WriteAllTextAsync(pending, ProtectedBytes);
        }
        var observer = new ComparisonProgressFile(path);
        try
        {
            observer.Observe(Marker);
            await observer.StopAsync().WaitAsync(Deadline, TimeProvider.System);
            await Assert.That(observer.HasWriteFailure).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(pending)).IsEqualTo(ProtectedBytes);
            await Assert.That(await File.ReadAllTextAsync(protectedFile)).IsEqualTo(ProtectedBytes);
            await Assert.That(new FileInfo(pending).LinkTarget is not null).IsEqualTo(symbolicLink);
            await Assert.That(File.Exists(path)).IsFalse();
        }
        finally
        {
            await observer.StopAsync().WaitAsync(Deadline, TimeProvider.System);
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task LinkedAncestorRejectsBeforeCreatingOrModifyingTargetFiles()
    {
        var root = ComparisonProgressFileTests.CreateRoot("keyload-progress-ancestor-");
        var outside = Directory.CreateDirectory(Path.Combine(root.FullName, "outside"));
        var protectedFile = Path.Combine(outside.FullName, ComparisonProgressLine.FileName);
        await File.WriteAllTextAsync(protectedFile, ProtectedBytes);
        var link = Path.Combine(root.FullName, "linked");
        Directory.CreateSymbolicLink(link, outside.FullName);
        var observer = new ComparisonProgressFile(Path.Combine(link, "new-directory", ComparisonProgressLine.FileName));
        try
        {
            observer.Observe(Marker);
            await observer.StopAsync().WaitAsync(Deadline, TimeProvider.System);
            await Assert.That(observer.HasWriteFailure).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(protectedFile)).IsEqualTo(ProtectedBytes);
            await Assert.That(Directory.GetFileSystemEntries(outside.FullName).Length).IsEqualTo(1);
            await Assert.That(new DirectoryInfo(link).LinkTarget).IsEqualTo(outside.FullName);
        }
        finally
        {
            await observer.StopAsync().WaitAsync(Deadline, TimeProvider.System);
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task LinkedDestinationIsPreservedWithoutReplacingItsTarget()
    {
        var root = ComparisonProgressFileTests.CreateRoot("keyload-progress-destination-");
        var protectedFile = Path.Combine(root.FullName, "outside.log");
        await File.WriteAllTextAsync(protectedFile, ProtectedBytes);
        var path = Path.Combine(root.FullName, ComparisonProgressLine.FileName);
        File.CreateSymbolicLink(path, protectedFile);
        var observer = new ComparisonProgressFile(path);
        try
        {
            observer.Observe(Marker);
            await observer.StopAsync().WaitAsync(Deadline, TimeProvider.System);
            await Assert.That(observer.HasWriteFailure).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(protectedFile)).IsEqualTo(ProtectedBytes);
            await Assert.That(new FileInfo(path).LinkTarget).IsEqualTo(protectedFile);
            await Assert.That(File.Exists(path + PendingSuffix)).IsFalse();
        }
        finally
        {
            await observer.StopAsync().WaitAsync(Deadline, TimeProvider.System);
            root.Delete(recursive: true);
        }
    }
}
