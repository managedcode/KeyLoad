using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class BenchmarkSessionContractTests
{
    private static BenchmarkSession Open(SessionRegressionTarget target, int concurrency = 5)
        => new(target, UnitBenchmarkOptions.Native(), new()
        {
            Records = 400,
            Operations = 100,
            Concurrency = concurrency
        }, true);

    [Test]
    public async Task NativeSessionContractKeepsOneDatasetAndMeasuresEveryInitialWriteWithoutWarmup()
    {
        await using var target = new SessionRegressionTarget();
        var session = Open(target);
        await Assert.That(target.Initializations).IsEqualTo(0);
        await Assert.That(target.Documents.Count).IsEqualTo(0);
        var token = TestContext.Current!.Execution.CancellationToken;
        var load = await session.LoadAsync(token);
        await Assert.That(load.Status).IsEqualTo("measured");
        await Assert.That(load.InitialRecords).IsEqualTo(0);
        await Assert.That(load.VerifiedRecords).IsEqualTo(400);
        await Assert.That(load.Lanes.Single().Measurement.Attempts).IsEqualTo(400);
        await Assert.That(load.Lanes.Single().Samples.Length).IsEqualTo(400);
        var sequential = await session.SequentialReadAsync(token);
        var random = await session.RandomReadAsync(token);
        await Assert.That(sequential.Status).IsEqualTo("measured");
        await Assert.That(random.Status).IsEqualTo("measured");
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Writes).IsEqualTo(400);
        await Assert.That((await session.IndexAsync(token)).Status).IsEqualTo("failed");
        await Assert.That((await session.SearchAsync(token)).Status).IsEqualTo("failed");
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }

    [Test]
    public async Task AcknowledgedButDiscardedInitialRecordsFailTheIndependentCompleteOracle()
    {
        await using var target = new SessionRegressionTarget { DiscardWrites = true };
        var session = Open(target);
        var token = TestContext.Current!.Execution.CancellationToken;
        var load = await session.LoadAsync(token);
        await Assert.That(load.Status).IsEqualTo("failed");
        await Assert.That(load.VerifiedRecords).IsEqualTo(0);
        await Assert.That((await session.RandomReadAsync(token)).Status).IsEqualTo("failed");
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }

    [Test]
    public async Task AWrongReadFailsActualAccountingAndPoisonsTheSessionWithoutResettingIt()
    {
        await using var target = new SessionRegressionTarget();
        var session = Open(target);
        var token = TestContext.Current!.Execution.CancellationToken;
        await session.LoadAsync(token);
        target.CorruptReads = true;
        var read = await session.SequentialReadAsync(token);
        await Assert.That(read.Status).IsEqualTo("failed");
        await Assert.That(read.Lanes.Single().Measurement.Failures > 0).IsTrue();
        await Assert.That(read.Lanes.Single().Samples.Any(sample => !sample.Success)).IsTrue();
        await Assert.That((await session.MixedAsync(token)).Status).IsEqualTo("failed");
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }

    [Test]
    [Arguments(false, 5)]
    [Arguments(true, 5)]
    [Arguments(false, 16)]
    [Arguments(true, 16)]
    public async Task MixedStreamsShareTheSameStateAndTheFinalOracleRejectsDroppedUpdates(bool discardUpdates, int concurrency)
    {
        await using var target = new SessionQueryRegressionTarget { DiscardUpdates = discardUpdates };
        var session = Open(target, concurrency);
        var token = TestContext.Current!.Execution.CancellationToken;
        await Assert.That((await session.LoadAsync(token)).Status).IsEqualTo("measured");
        target.HoldMixedStreams = true;
        var mixed = await session.MixedAsync(token);
        await Assert.That(mixed.Status).IsEqualTo(discardUpdates ? "failed" : "measured");
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Documents.Count).IsEqualTo(400);
        await Assert.That(target.Queries).IsEqualTo(100);
        await Assert.That(mixed.Lanes.Length).IsEqualTo(5);
        foreach (var lane in mixed.Lanes)
        {
            var admitted = concurrency / 5 + (lane.Id == "create" && concurrency % 5 != 0 ? 1 : 0);
            await Assert.That(lane.Samples.All(sample => sample.Worker >= 0 && sample.Worker < admitted)).IsTrue();
            await Assert.That(lane.FirstStartedMs).IsEqualTo(lane.Samples.Min(sample => sample.StartedMs));
        }
        await Assert.That(mixed.Lanes.All(lane => lane.Measurement.Successes == 100 && lane.Samples.Length == 100)).IsTrue();
        if (!discardUpdates)
        {
            await Assert.That(mixed.VerifiedRecords).IsEqualTo(400);
            await Assert.That((await session.VerifyAsync(token)).Status).IsEqualTo("measured");
        }
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }

    [Test]
    public async Task GenuineNativeIndexAbsenceLeavesTheLoadedSessionAvailableForReadsAndQueries()
    {
        await using var target = new SessionQueryRegressionTarget { NativeIndexUnavailable = true };
        var session = Open(target);
        var token = TestContext.Current!.Execution.CancellationToken;
        await Assert.That((await session.LoadAsync(token)).Status).IsEqualTo("measured");
        await Assert.That((await session.IndexAsync(token)).Status).IsEqualTo("unavailable");
        await Assert.That((await session.SequentialReadAsync(token)).Status).IsEqualTo("measured");
        await Assert.That((await session.SearchAsync(token)).Status).IsEqualTo("measured");
        await Assert.That(target.Initializations).IsEqualTo(1);
    }

    [Test]
    public async Task FailedInitialLoadRetainsOriginalAcknowledgementsAndCannotReinitializeItsDatabase()
    {
        await using var target = new SessionRegressionTarget { DiscardWrites = true };
        var session = Open(target);
        var token = TestContext.Current!.Execution.CancellationToken;
        var failed = await session.LoadAsync(token);
        await Assert.That(failed.Status).IsEqualTo("failed");
        await Assert.That(failed.Lanes.Single().Samples.Count(sample => sample.Success)).IsEqualTo(400);
        await Assert.That((await session.LoadAsync(token)).Status).IsEqualTo("failed");
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Writes).IsEqualTo(400);
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }


    [Test]
    public async Task ProductionSessionRejectsNoncanonicalConcurrencyBeforeNativeInitialization()
    {
        await using var target = new SessionRegressionTarget();
        await Assert.That(() => new BenchmarkSession(target, UnitBenchmarkOptions.Native(),
            new BenchmarkSessionOptions { Concurrency = 17 })).Throws<InvalidOperationException>();
        await Assert.That(target.Initializations).IsEqualTo(0);
        await Assert.That(target.Writes).IsEqualTo(0);
    }


    [Test]
    public async Task SynchronousNativeOperationsStillUseEveryConfiguredClientAndItsDisjointOrdinals()
    {
        await using var target = new SessionQueryRegressionTarget { SynchronousOperations = true };
        var session = Open(target, concurrency: 16);
        var token = TestContext.Current!.Execution.CancellationToken;
        var reports = new[]
        {
            await session.LoadAsync(token), await session.SequentialReadAsync(token),
            await session.RandomReadAsync(token), await session.SearchAsync(token)
        };
        foreach (var report in reports)
        {
            await Assert.That(report.Status).IsEqualTo("measured");
            var samples = report.Lanes.Single().Samples;
            await Assert.That(samples.Select(sample => sample.Worker).Distinct().Order())
                .IsEquivalentTo(Enumerable.Range(0, 16));
            await Assert.That(samples.All(sample => sample.Success && sample.Worker == sample.Operation % 16)).IsTrue();
            await Assert.That(samples.Select(sample => sample.Operation).Distinct().Count()).IsEqualTo(samples.Length);
        }
        await Assert.That(target.Initializations).IsEqualTo(1);
        await Assert.That(target.Writes).IsEqualTo(400);
        await Assert.That(target.Opened).IsEqualTo(target.Closed);
    }

}
