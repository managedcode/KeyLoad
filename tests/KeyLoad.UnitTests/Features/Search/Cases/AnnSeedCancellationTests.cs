using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedCancellationTests
{
    [Test]
    public async Task AlreadyCanceledBudgetRejectsBeforeSeedPublication()
    {
        using var database = AnnSeedTestSupport.Create(2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),             cancellationToken: cancellation.Token);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            AnnSeedTestSupport.Capture(database, budget: budget));

        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(budget.ReadBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task InProgressChargedCaptureCancelsAndFollowingCaptureRemainsHealthy()
    {
        using var database = AnnSeedTestSupport.Create(10_000);
        var observation = AnnSeedCancellationCapture.CaptureAndCancel(database);

        await Assert.That(observation.Elapsed).IsLessThan(
            TimeSpan.FromSeconds(AnnSeedCancellationThread.TimeoutSeconds));
        await Assert.That(observation.ReadBytes).IsGreaterThan(0L);
        await Assert.That(observation.ObservedReadBytes).IsGreaterThan(0L);
        await Assert.That(observation.CancellationRequested).IsTrue();
        await Assert.That(observation.Seed).IsNull();
        await Assert.That(observation.CaptureCancellation?.GetType())
            .IsEqualTo(typeof(OperationCanceledException));
        var following = AnnSeedTestSupport.Capture(database);
        await Assert.That(following.Records.Length).IsEqualTo(10_000);
    }
}
