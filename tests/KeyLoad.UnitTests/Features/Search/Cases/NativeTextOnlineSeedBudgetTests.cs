using KeyLoad.Core;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextOnlineSeedBudgetTests
{
    [Test]
    public async Task CombinedOriginalGrantRefusesDuringActualSeedThenJoinedPinAllowsFullHealthySeed()
    {
        using var database = new TestDatabase(nativeReplicaAdmission: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var fixture = await NativeTextOnlineSeedFixture.CreateAsync(database, token);
        var metadataBytes = await NativeTextOnlineSeedAssertions.HealthyAsync(database, fixture, token);
        var limits = database.Database.Limits with
        {
            MaxQueryReadBytes = checked(metadataBytes + fixture.FirstRetainedAndBothNativeRowsBytes() - 1)
        };
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits),
            cancellationToken: token);
        using (var refused = NativeTextOnlineSourcePin.Capture(database.Database,
            NativeTextMaintenanceTestValues.Principal, fixture.Pin, budget))
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => refused.ReadSeed());
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        }
        _ = await NativeTextOnlineSeedAssertions.HealthyAsync(database, fixture, token);
    }

    [Test]
    public async Task ActualCapturedSeedCancellationJoinsBeforeFreshFullHealthySeed()
    {
        using var database = new TestDatabase(nativeReplicaAdmission: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var fixture = await NativeTextOnlineSeedFixture.CreateAsync(database, token);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            cancellationToken: source.Token);
        using (var cancelled = NativeTextOnlineSourcePin.Capture(database.Database,
            NativeTextMaintenanceTestValues.Principal, fixture.Pin, budget))
        {
            await source.CancelAsync();
            var failure = Assert.ThrowsExactly<OperationCanceledException>(() => cancelled.ReadSeed());
            await Assert.That(failure.CancellationToken.IsCancellationRequested).IsTrue();
        }
        _ = await NativeTextOnlineSeedAssertions.HealthyAsync(database, fixture, token);
    }
}
