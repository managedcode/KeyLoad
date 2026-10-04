using System.Globalization;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-LIVE-002/003: one atomic latest file is retained while work and cancellation are active.</summary>
internal sealed class ComparisonProgressFileTests
{
    private const string First = "KeyLoadBenchmarkProgress phase=measure repetition=1 completed=0 total=10 failed=0 elapsedSeconds=0";
    private const string Last = "KeyLoadBenchmarkProgress phase=measure repetition=1 completed=10 total=10 failed=2 elapsedSeconds=30";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Test]
    public async Task PersistsBeforeStopAndRetainsOnlyLatestValidLine()
    {
        var root = CreateRoot("keyload-progress-file-");
        var path = Path.Combine(root.FullName, ComparisonProgressLine.FileName);
        var observer = new ComparisonProgressFile(path);
        try
        {
            using var deadline = new CancellationTokenSource(Deadline);
            observer.Observe(First);
            await WaitForSnapshotAsync(path, First, deadline.Token);
            observer.Observe(First + " private=payload");
            observer.Observe(Last);
            await observer.StopAsync().WaitAsync(deadline.Token);
            await Assert.That((await File.ReadAllTextAsync(path, deadline.Token)).TrimEnd()).IsEqualTo(Last);
            await Assert.That(Directory.GetFiles(root.FullName).Length).IsEqualTo(1);
            await Assert.That(new FileInfo(path).Length).IsLessThanOrEqualTo(ComparisonProgressLine.MaximumCharacters + 2);
            await Assert.That(observer.HasWriteFailure).IsFalse();
            observer.Observe(First);
            await observer.StopAsync().WaitAsync(deadline.Token);
            await Assert.That((await File.ReadAllTextAsync(path, deadline.Token)).TrimEnd()).IsEqualTo(Last);
        }
        finally
        {
            await observer.StopAsync().WaitAsync(Deadline);
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ConcurrentSnapshotsRemainAtomicAndFinalSnapshotWins()
    {
        var root = CreateRoot("keyload-progress-concurrent-");
        var path = Path.Combine(root.FullName, ComparisonProgressLine.FileName);
        var observer = new ComparisonProgressFile(path);
        try
        {
            using var deadline = new CancellationTokenSource(Deadline);
            await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() => observer.Observe(
                First.Replace("completed=0", "completed=" + (index % 10).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)), deadline.Token)));
            observer.Observe(Last);
            var stop = observer.StopAsync();
            await Assert.That(ReferenceEquals(stop, observer.StopAsync())).IsTrue();
            await stop.WaitAsync(deadline.Token);
            await Assert.That((await File.ReadAllTextAsync(path, deadline.Token)).TrimEnd()).IsEqualTo(Last);
            await Assert.That(Directory.GetFiles(root.FullName).Length).IsEqualTo(1);
        }
        finally
        {
            await observer.StopAsync().WaitAsync(Deadline);
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task FileFailureIsObservedWithoutReplacingWorkloadOutcome()
    {
        var root = CreateRoot("keyload-progress-failure-");
        var blocked = Path.Combine(root.FullName, "blocked");
        await File.WriteAllTextAsync(blocked, "occupied");
        var observer = new ComparisonProgressFile(Path.Combine(blocked, ComparisonProgressLine.FileName));
        try
        {
            observer.Observe(First);
            await observer.StopAsync().WaitAsync(Deadline);
            await Assert.That(observer.HasWriteFailure).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(blocked)).IsEqualTo("occupied");
        }
        finally
        {
            await observer.StopAsync().WaitAsync(Deadline);
            root.Delete(recursive: true);
        }
    }

    internal static async Task WaitForSnapshotAsync(string path, string expected, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path) && (await File.ReadAllTextAsync(path, token)).TrimEnd() == expected)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), token);
        }
    }

    internal static DirectoryInfo CreateRoot(string prefix)
        => new(Resolve(Directory.CreateTempSubdirectory(prefix)));

    private static string Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(Resolve(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }
}
