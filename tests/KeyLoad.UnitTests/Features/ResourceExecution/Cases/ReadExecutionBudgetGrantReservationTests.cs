using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadExecutionBudgetGrantReservationTests
{
    private const string ExaminedExceededDiagnostic = "The read execution examined-record budget is exceeded.";
    [Test]
    public async Task ExactLeafGrantsShareOneAggregateNativeByteLedger()
    {
        var maximumBytes = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length
            + ReadExecutionBudgetGrantSeed.SecondKey.Length + ReadExecutionBudgetGrantSeed.SecondValue.Length;
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase(new() { MaxQueryReadBytes = maximumBytes });
        var result = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(database.Database.Limits);
            var firstGrant = budget.CreateReadGrant(ReadExecutionBudgetGrantSeed.FirstKey.Length
                + ReadExecutionBudgetGrantSeed.FirstValue.Length, 1);
            var secondGrant = budget.CreateReadGrant(ReadExecutionBudgetGrantSeed.SecondKey.Length
                + ReadExecutionBudgetGrantSeed.SecondValue.Length, 1);
            var firstFound = firstGrant.ReadValue(view, ReadExecutionBudgetGrantSeed.FirstKey, static _ => { });
            var firstCharge = budget.ReadBytes;
            var secondFound = secondGrant.ReadValue(view, ReadExecutionBudgetGrantSeed.SecondKey, static _ => { });
            return (firstFound, secondFound, firstCharge, TotalCharge: budget.ReadBytes);
        });

        await Assert.That(result.firstFound).IsTrue();
        await Assert.That(result.secondFound).IsTrue();
        await Assert.That(result.firstCharge).IsEqualTo(ReadExecutionBudgetGrantSeed.FirstKey.Length
            + ReadExecutionBudgetGrantSeed.FirstValue.Length);
        await Assert.That(result.TotalCharge).IsEqualTo(maximumBytes);
    }

    [Test]
    public async Task FixedReservationsCannotBeConsumedByRootOrLaterGrants()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase(new() { MaxQueryReadBytes = 64 });
        var rootCharge = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(_ =>
        {
            var budget = new ReadExecutionBudget(database.Database.Limits);
            budget.CreateReadGrant(64, database.Database.Limits.MaxScanRecords);
            budget.ChargeBytes(1);
            return true;
        }));
        var additionalGrant = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(_ =>
        {
            var budget = new ReadExecutionBudget(database.Database.Limits);
            budget.CreateReadGrant(64, database.Database.Limits.MaxScanRecords);
            budget.CreateReadGrant(1, 0);
            return true;
        }));
        var recordGrant = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(_ =>
        {
            var budget = new ReadExecutionBudget(database.Database.Limits);
            budget.CreateReadGrant(0, database.Database.Limits.MaxScanRecords);
            budget.CreateReadGrant(0, 1);
            return true;
        }));

        await Assert.That(rootCharge.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(additionalGrant.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(recordGrant.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(recordGrant.Message).IsEqualTo(ExaminedExceededDiagnostic);
    }
}
