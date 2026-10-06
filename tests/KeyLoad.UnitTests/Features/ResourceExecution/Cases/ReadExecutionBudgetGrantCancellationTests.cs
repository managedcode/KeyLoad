using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadExecutionBudgetGrantCancellationTests
{
    private const int QueryDeadlineSeconds = 30;

    [Test]
    public async Task GrantRangeUsesRootCancellationAndLeavesStoreReadable()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        using var cancellation = new CancellationTokenSource();
        var observed = 0;
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()), cancellationToken: cancellation.Token);
            var grant = budget.CreateReadGrant(64, 2);
            return grant.VisitRange(view, ReadExecutionBudgetGrantSeed.Prefix, 2, (_, _) =>
            {
                observed++;
                cancellation.Cancel();
                return true;
            });
        }));
        var following = database.Store.Read(view => new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new())).Scan(view,
            ReadExecutionBudgetGrantSeed.Prefix, 2));

        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(observed).IsEqualTo(1);
        await Assert.That(following.Records.Length).IsEqualTo(2);
        await Assert.That(following.HasMore).IsFalse();
    }

    [Test]
    public async Task GrantRangeUsesRootDeadlineDuringNativeTraversal()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var clock = new AdvancingClock();
        var visited = 0;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new() { QueryDeadlineSeconds = QueryDeadlineSeconds }), clock);
            var grant = budget.CreateReadGrant(64, 2);
            return grant.VisitRange(view, ReadExecutionBudgetGrantSeed.Prefix, 2, (_, _) =>
            {
                visited++;
                clock.Advance(TimeSpan.FromSeconds(QueryDeadlineSeconds + 1));
                return true;
            });
        }));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(visited).IsEqualTo(1);
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        internal void Advance(TimeSpan duration) => timestamp += duration.Ticks;
    }
}
