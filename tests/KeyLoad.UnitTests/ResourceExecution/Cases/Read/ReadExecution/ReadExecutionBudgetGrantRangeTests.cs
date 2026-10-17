using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadExecutionBudgetGrantRangeTests
{
    [Test]
    public async Task ExactRangeGrantIncludesChargedNativeLookaheadAttempt()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var maximumBytes = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length
            + ReadExecutionBudgetGrantSeed.SecondKey.Length + ReadExecutionBudgetGrantSeed.SecondValue.Length;
        var result = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()));
            var grant = budget.CreateReadGrant(maximumBytes, 2);
            var scan = grant.VisitRange(view, ReadExecutionBudgetGrantSeed.Prefix, 1, static (_, _) => true);
            return (Scan: scan, RootBytes: budget.ReadBytes, GrantBytes: grant.ReadBytes, grant.ExaminedRecords);
        });

        await Assert.That(result.Scan.Records).IsEqualTo(1);
        await Assert.That(result.Scan.HasMore).IsTrue();
        await Assert.That(result.RootBytes).IsEqualTo(maximumBytes);
        await Assert.That(result.GrantBytes).IsEqualTo(maximumBytes);
        await Assert.That(result.ExaminedRecords).IsEqualTo(2);
    }

    [Test]
    public async Task OneUnderRangeAttemptGrantRejectsLookaheadBeforeNextVisitor()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var maximumBytes = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length
            + ReadExecutionBudgetGrantSeed.SecondKey.Length + ReadExecutionBudgetGrantSeed.SecondValue.Length;
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()));
        var grant = budget.CreateReadGrant(maximumBytes, 1);
        var visited = 0;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
            grant.VisitRange(view, ReadExecutionBudgetGrantSeed.Prefix, 1, (_, _) =>
            {
                visited++;
                return true;
            })));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(visited).IsEqualTo(1);
        await Assert.That(budget.ReadBytes).IsEqualTo(ReadExecutionBudgetGrantSeed.FirstKey.Length
            + ReadExecutionBudgetGrantSeed.FirstValue.Length);
        await Assert.That(grant.ReadBytes).IsEqualTo(ReadExecutionBudgetGrantSeed.FirstKey.Length
            + ReadExecutionBudgetGrantSeed.FirstValue.Length);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(1);
    }

    [Test]
    public async Task ZeroGrantAllowsEmptyNativeRangeWithoutChargingBytes()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var result = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()));
            var grant = budget.CreateReadGrant(0, 0);
            var scan = grant.VisitRange(view, ReadExecutionBudgetGrantSeed.EmptyRange, 1, static (_, _) => true);
            return (scan.Records, budget.ReadBytes);
        });

        await Assert.That(result.Records).IsEqualTo(0);
        await Assert.That(result.ReadBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task OneByteOverRangeGrantRejectsBeforeNativeVisitorAndAggregateDebit()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var maximumBytes = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length - 1;
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()));
        var grant = budget.CreateReadGrant(maximumBytes, 1);
        var visited = false;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
            grant.VisitRange(view, ReadExecutionBudgetGrantSeed.Prefix, 1, (_, _) =>
            {
                visited = true;
                return true;
            })));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(visited).IsFalse();
        await Assert.That(budget.ReadBytes).IsEqualTo(0L);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(0);
    }

}
