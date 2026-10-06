using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonStatisticsTests
{
    private const string FirstCompletedMessageId = "m1";
    private const string TimeoutErrorType = "TimeoutException";
    private const string SecondCompletedMessageId = "m2";

    [Test]
    public async Task FailedAttemptsAndTimeoutLatencyRemainVisibleAndDoNotInflateUsefulThroughput()
    {
        OperationSample[] samples = [new(0, 0, 0, 1, true, null, 128, FirstCompletedMessageId, new(0.2, 0.5, 0.3)),
            new(1, 0, 1, 101, false, TimeoutErrorType, 128, null, null),
            new(2, 0, 101, 103, true, null, 128, SecondCompletedMessageId, new(0.5, 1, 0.5))];
        var result = ComparisonRunner.Summarize(samples, 2);
        await Assert.That(result.Attempts).IsEqualTo(3);
        await Assert.That(result.Successes).IsEqualTo(2);
        await Assert.That(result.Failures).IsEqualTo(1);
        await Assert.That(result.UsefulOperationsPerSecond).IsEqualTo(1);
        await Assert.That(result.Latency.P99Ms).IsEqualTo(100);
        await Assert.That(result.UniqueCompletedMessages).IsEqualTo(2);
        await Assert.That(result.Enqueue).IsNotNull();
    }
}
