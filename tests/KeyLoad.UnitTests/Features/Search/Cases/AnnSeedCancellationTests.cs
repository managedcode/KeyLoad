using System.Diagnostics;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedCancellationTests
{
    private static readonly TimeSpan CancellationJoinTimeout = TimeSpan.FromSeconds(15);

    [Test]
    public async Task AlreadyCanceledBudgetRejectsBeforeSeedPublication()
    {
        using var database = AnnSeedTestSupport.Create(2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var budget = new ReadExecutionBudget(database.Database.Limits,
            cancellationToken: cancellation.Token);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            AnnSeedTestSupport.Capture(database, budget: budget));

        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(budget.ReadBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task InProgressChargedCaptureCancelsAndFollowingCaptureRemainsHealthy()
    {
        using var database = AnnSeedTestSupport.Create(10_000);
        var observation = await CaptureAndCancelAsync(database);

        await Assert.That(observation.Elapsed).IsLessThan(CancellationJoinTimeout);
        await Assert.That(observation.ReadBytes).IsGreaterThan(0L);
        var following = AnnSeedTestSupport.Capture(database);
        await Assert.That(following.Records.Length).IsEqualTo(10_000);
    }

    private static async Task<(TimeSpan Elapsed, long ReadBytes)> CaptureAndCancelAsync(TestDatabase database)
    {
        var cancellation = new CancellationTokenSource();
        var budget = new ReadExecutionBudget(database.Database.Limits,
            cancellationToken: cancellation.Token);
        var started = Stopwatch.GetTimestamp();
        var observer = CancelAfterFirstChargedReadAsync(budget, cancellation);

        try
        {
            Assert.ThrowsExactly<OperationCanceledException>(() =>
                AnnSeedTestSupport.Capture(database, budget: budget));
            await observer.WaitAsync(CancellationJoinTimeout);
        }
        finally
        {
            try
            {
                await cancellation.CancelAsync();
            }
            finally
            {
                try
                {
                    await observer;
                }
                finally
                {
                    cancellation.Dispose();
                }
            }
        }

        return (Stopwatch.GetElapsedTime(started), budget.ReadBytes);
    }

    private static async Task CancelAfterFirstChargedReadAsync(ReadExecutionBudget budget,
        CancellationTokenSource cancellation)
    {
        var started = Stopwatch.GetTimestamp();
        while (budget.ReadBytes == 0)
        {
            if (Stopwatch.GetElapsedTime(started) > CancellationJoinTimeout)
            {
                throw new TimeoutException("The seed capture did not charge a read before cancellation.");
            }
            await Task.Delay(1, CancellationToken.None);
        }
        await cancellation.CancelAsync();
    }
}
