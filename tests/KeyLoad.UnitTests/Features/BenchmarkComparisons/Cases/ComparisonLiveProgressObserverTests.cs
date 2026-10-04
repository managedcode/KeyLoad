using System.Globalization;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonLiveProgressObserverTests
{
    private const int Attempts = 16;

    [Test]
    public async Task AcBcLive001ConcurrentSettledFileAttemptsRetainFailuresAndInvariantLatestCounts()
    {
        using var file = new ComparisonLiveProgressFile();
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            await using var observer = new ComparisonProgressObserver(line => File.WriteAllText(file.Path, line));
            observer.Begin(ComparisonProgressPhase.Measure, 2, Attempts);
            await Parallel.ForAsync(0, Attempts, TestContext.Current!.Execution.CancellationToken, (attempt, token) =>
                WriteAttemptAsync(file, observer, attempt, token));
            observer.Complete();
            var latest = await File.ReadAllTextAsync(file.Path, TestContext.Current!.Execution.CancellationToken);
            await ComparisonLiveProgressAssertions.AssertClosedAsync(latest);
            await Assert.That(latest.Contains("phase=complete repetition=2 completed=16 total=16 failed=1", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public async Task AcBcLive002InvalidStateIsRejectedBeforeReplacingTheAcceptedFile()
    {
        using var file = new ComparisonLiveProgressFile();
        await using var observer = new ComparisonProgressObserver(line => File.WriteAllText(file.Path, line));
        observer.Begin(ComparisonProgressPhase.Initialize, 0);
        var accepted = await File.ReadAllTextAsync(file.Path, TestContext.Current!.Execution.CancellationToken);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => observer.Begin((ComparisonProgressPhase)int.MaxValue, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => observer.Begin(ComparisonProgressPhase.Measure, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => observer.Begin(ComparisonProgressPhase.Measure, 0, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => observer.Begin(ComparisonProgressPhase.Measure, 0, 1, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => observer.Begin(ComparisonProgressPhase.Measure, 0, 1, 1, 2));
        await Assert.That(await File.ReadAllTextAsync(file.Path, TestContext.Current!.Execution.CancellationToken)).IsEqualTo(accepted);
    }

    [Test]
    public async Task AcBcLive002ActualMissingDirectoryOutputDoesNotAbortObserverLifetime()
    {
        using var file = new ComparisonLiveProgressFile();
        var missing = file.MissingPath;
        await using var observer = new ComparisonProgressObserver(line => File.WriteAllText(missing, line));
        observer.Begin(ComparisonProgressPhase.Warmup, 1, 1);
        observer.Settle(success: true);
        observer.Complete();
        await Assert.That(File.Exists(missing)).IsFalse();
    }

    private static async ValueTask WriteAttemptAsync(ComparisonLiveProgressFile file, ComparisonProgressObserver observer,
        int attempt, CancellationToken cancellationToken)
    {
        var success = false;
        var path = file.Path + attempt.ToString(CultureInfo.InvariantCulture);
        try
        {
            if (attempt == 0)
            {
                await File.ReadAllTextAsync(path, cancellationToken);
            }
            else
            {
                await File.WriteAllTextAsync(path, attempt.ToString(CultureInfo.InvariantCulture), cancellationToken);
            }
            success = true;
        }
        catch (FileNotFoundException)
        {
            // The first real file attempt fails; remaining writes complete concurrently.
        }
        finally
        {
            observer.Settle(success);
        }
    }
}
