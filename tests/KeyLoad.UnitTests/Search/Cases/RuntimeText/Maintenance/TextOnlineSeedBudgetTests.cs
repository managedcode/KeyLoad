using KeyLoad.Server.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextOnlineSeedBudgetTests
{
    [Test]
    public async Task CombinedOriginalGrantRefusesDuringActualSeedThenJoinedPinAllowsFullHealthySeed()
    {
        using var database = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var fixture = await TextOnlineSeedFixture.CreateAsync(database, token);
        var metadataBytes = await TextOnlineSeedAssertions.HealthyAsync(database, fixture, token);
        var limits = database.Database.Limits with
        {
            MaxQueryReadBytes = checked(metadataBytes + fixture.FirstRetainedAndBothRowsBytes() - 1)
        };
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits),
            cancellationToken: token);
        using (var refused = TextOnlineSourcePin.Capture(database.Database,
            TextMaintenanceTestValues.Principal, fixture.Pin, budget))
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => refused.ReadSeed());
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }
        _ = await TextOnlineSeedAssertions.HealthyAsync(database, fixture, token);
    }

    [Test]
    public async Task ActualCapturedSeedCancellationJoinsBeforeFreshFullHealthySeed()
    {
        using var database = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var fixture = await TextOnlineSeedFixture.CreateAsync(database, token);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            cancellationToken: source.Token);
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
