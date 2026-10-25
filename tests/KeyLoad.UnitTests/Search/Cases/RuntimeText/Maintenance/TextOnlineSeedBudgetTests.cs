using KeyLoad.Server.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextOnlineSeedBudgetTests
{

    [Test]
    public async Task ActualCapturedSeedCancellationJoinsBeforeFreshFullHealthySeed()
    {
        using var database = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var fixture = await TextOnlineSeedFixture.CreateAsync(database, token);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        var budget = new ReadExecutionBudget(cancellationToken: source.Token);
        using (var cancelled = TextOnlineSourcePin.Capture(database.Database,
            TextMaintenanceTestValues.Principal, fixture.Pin, budget))
        {
            await source.CancelAsync();
            var failure = Assert.ThrowsExactly<OperationCanceledException>(() => cancelled.ReadSeed());
            await Assert.That(failure.CancellationToken.IsCancellationRequested).IsTrue();
        }
        _ = await TextOnlineSeedAssertions.HealthyAsync(database, fixture, token);
    }
}
