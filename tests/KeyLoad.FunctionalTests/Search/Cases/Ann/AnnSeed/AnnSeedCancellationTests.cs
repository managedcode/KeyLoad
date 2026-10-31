namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnSeedCancellationTests
{
    [Test]
    public async Task AlreadyCanceledBudgetRejectsBeforeSeedPublication()
    {
        using var database = AnnSeedTestSupport.Create(2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var budget = new ReadExecutionBudget(cancellationToken: cancellation.Token);
        var reads = database.Store.GetReadDiagnostics();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            AnnSeedTestSupport.Capture(database, budget: budget));

        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(database.Store.GetReadDiagnostics()).IsEqualTo(reads);
    }

    [Test]
    public async Task InProgressCaptureCancelsAndFollowingCaptureRemainsHealthy()
    {
        using var database = AnnSeedTestSupport.Create(10_000);
        var observation = AnnSeedCancellationCapture.CaptureAndCancel(database);

        await Assert.That(observation.Elapsed).IsLessThan(
            TimeSpan.FromSeconds(AnnSeedCancellationThread.TimeoutSeconds));
        await Assert.That(observation.ObservedEntries).IsGreaterThan(0L);
        await Assert.That(observation.CancellationRequested).IsTrue();
        await Assert.That(observation.Seed).IsNull();
        await Assert.That(observation.CaptureCancellation?.GetType())
            .IsEqualTo(typeof(OperationCanceledException));
        var following = AnnSeedTestSupport.Capture(database);
        await Assert.That(following.Records.Length).IsEqualTo(10_000);
    }
}
