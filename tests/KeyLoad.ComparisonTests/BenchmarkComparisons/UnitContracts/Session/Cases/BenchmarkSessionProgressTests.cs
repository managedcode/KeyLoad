using System.Collections.Concurrent;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Controlled development contract flows; these callbacks are not database performance evidence.</summary>
internal sealed class BenchmarkSessionProgressTests
{
    private const int Records = 2048;
    private const int CancellationRecords = 4096;
    private const int ProgressBoundary = 1024;
    private const int Operations = 100;
    private const int Clients = 16;

    [Test]
    public async Task ActualLoadCallbacksRetainTerminalAttemptsAndAcknowledgementsWithoutWarmup()
    {
        await using var target = new SessionChecksTarget();
        var progress = new ConcurrentQueue<DocumentComparisonProgress>();
        var session = Open(target, Records, progress.Enqueue);
        var report = await session.LoadAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(report.Status).IsEqualTo("measured");
        await Assert.That(progress.Count).IsGreaterThanOrEqualTo(2);
        var terminal = progress.Last();
        await Assert.That(terminal.Planned).IsEqualTo(Records);
        await Assert.That(terminal.Attempts).IsEqualTo(Records);
        await Assert.That(terminal.Acknowledged).IsEqualTo(target.Writes);
        await Assert.That(terminal.Failed + terminal.Canceled).IsEqualTo(0);
        await Assert.That((long)report.Lanes.Single().Samples.Length).IsEqualTo(terminal.Attempts);
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }

    [Test]
    public async Task FinalOracleFailureDoesNotEraseTheActualSuccessfulAcknowledgementProgress()
    {
        await using var target = new SessionChecksTarget { DiscardWrites = true };
        var progress = new ConcurrentQueue<DocumentComparisonProgress>();
        var session = Open(target, Records, progress.Enqueue);
        var report = await session.LoadAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(report.Status).IsEqualTo("failed");
        await Assert.That(report.VerifiedRecords).IsEqualTo(0);
        await Assert.That(target.Documents.Count).IsEqualTo(0);
        await Assert.That(progress.Last().Acknowledged).IsEqualTo(Records);
        await Assert.That(report.Lanes.Single().Measurement.Successes).IsEqualTo(Records);
        await Assert.That(report.Lanes.Single().Samples.All(sample => sample.Success)).IsTrue();
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }

    [Test]
    public async Task CallbackCancellationSettlesRealAttemptsAndPoisonsTheSessionWithoutReseeding()
    {
        await using var target = new SessionChecksTarget();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var progress = new ConcurrentQueue<DocumentComparisonProgress>();
        var session = Open(target, CancellationRecords, value =>
        {
            progress.Enqueue(value);
            if (value.Acknowledged >= ProgressBoundary)
            {
                cancellation.Cancel();
            }
        });
        var report = await session.LoadAsync(cancellation.Token);
        var lane = report.Lanes.Single();
        var terminal = progress.Last();
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(report.Status).IsEqualTo("failed");
        await Assert.That(terminal.Acknowledged).IsGreaterThanOrEqualTo(ProgressBoundary);
        await Assert.That(terminal.Acknowledged).IsLessThan(CancellationRecords);
        await Assert.That(terminal.Attempts).IsEqualTo(lane.Measurement.Attempts);
        await Assert.That(terminal.Acknowledged).IsEqualTo(lane.Measurement.Successes);
        await Assert.That(terminal.Failed + terminal.Canceled).IsEqualTo(lane.Measurement.Failures);
        await Assert.That((long)lane.Samples.Length).IsEqualTo(terminal.Attempts);
        await Assert.That((long)target.Documents.Count).IsEqualTo(terminal.Acknowledged);
        await Assert.That((await session.LoadAsync(TestContext.Current!.Execution.CancellationToken)).Status).IsEqualTo("failed");
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }

    private static BenchmarkSession Open(SessionChecksTarget target, int records, Action<DocumentComparisonProgress> progress)
        => new(target, UnitBenchmarkOptions.Native(), new BenchmarkSessionOptions
        {
            Records = records,
            Operations = Operations,
            Concurrency = Clients
        }, development: true, progress: progress);
}
