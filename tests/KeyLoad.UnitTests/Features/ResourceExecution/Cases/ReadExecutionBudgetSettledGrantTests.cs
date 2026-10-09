using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadExecutionBudgetSettledGrantTests
{
    [Test]
    public async Task SettledNativeReadReturnsOnlyUnusedReservationAndPreservesConsumedLedger()
    {
        var firstBytes = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length;
        var secondBytes = ReadExecutionBudgetGrantSeed.SecondKey.Length + ReadExecutionBudgetGrantSeed.SecondValue.Length;
        var maximum = firstBytes + secondBytes;
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase(new() { MaxQueryReadBytes = maximum });
        var result = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
            var first = budget.CreateReadGrant(maximum, database.Database.Limits.MaxScanRecords);
            var firstFound = first.ReadValue(view, ReadExecutionBudgetGrantSeed.FirstKey, static _ => { });
            budget.CompleteReadGrant(first);
            var remaining = budget.RemainingReadGrantBytes;
            var second = budget.CreateReadGrant(remaining, budget.RemainingReadGrantRecords);
            var secondFound = second.ReadValue(view, ReadExecutionBudgetGrantSeed.SecondKey, static _ => { });
            budget.CompleteReadGrant(second);
            var excess = Assert.ThrowsExactly<KeyLoadException>(() => budget.ChargeBytes(1));
            return (firstFound, secondFound, remaining, Total: budget.ReadBytes, excess.Code);
        });
        await Assert.That(result.firstFound).IsTrue();
        await Assert.That(result.secondFound).IsTrue();
        await Assert.That(result.remaining).IsEqualTo((long)secondBytes);
        await Assert.That(result.Total).IsEqualTo((long)maximum);
        await Assert.That(result.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task ClosedGrantCannotReadNativeDataOrReturnItsReservationTwice()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var result = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
            var grant = budget.CreateReadGrant(database.Database.Limits.MaxQueryReadBytes, database.Database.Limits.MaxScanRecords);
            grant.ReadValue(view, ReadExecutionBudgetGrantSeed.FirstKey, static _ => { });
            budget.CompleteReadGrant(grant);
            var before = budget.ReadBytes;
            var remaining = budget.RemainingReadGrantBytes;
            var repeat = Assert.ThrowsExactly<ArgumentException>(() => budget.CompleteReadGrant(grant));
            var stale = Assert.ThrowsExactly<ArgumentException>(() =>
                grant.ReadValue(view, ReadExecutionBudgetGrantSeed.SecondKey, static _ => { }));
            return (before, After: budget.ReadBytes, remaining, AfterRemaining: budget.RemainingReadGrantBytes,
                RepeatParameter: repeat.ParamName, StaleParameter: stale.ParamName);
        });
        await Assert.That(result.After).IsEqualTo(result.before);
        await Assert.That(result.AfterRemaining).IsEqualTo(result.remaining);
        await Assert.That(result.RepeatParameter).IsEqualTo("grant");
        await Assert.That(result.StaleParameter).IsEqualTo("grant");
    }

    [Test]
    public async Task CompletionDoesNotReleaseAnotherUnsettledNativeChildReservation()
    {
        var maximum = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length;
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase(new() { MaxQueryReadBytes = maximum });
        var result = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
            var pending = budget.CreateReadGrant(maximum, 1);
            var settledEmpty = budget.CreateReadGrant(0, 0);
            budget.CompleteReadGrant(settledEmpty);
            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => budget.CreateReadGrant(1, 0));
            var found = pending.ReadValue(view, ReadExecutionBudgetGrantSeed.FirstKey, static _ => { });
            budget.CompleteReadGrant(pending);
            return (rejected.Code, found, Total: budget.ReadBytes);
        });
        await Assert.That(result.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(result.found).IsTrue();
        await Assert.That(result.Total).IsEqualTo((long)maximum);
    }
}
