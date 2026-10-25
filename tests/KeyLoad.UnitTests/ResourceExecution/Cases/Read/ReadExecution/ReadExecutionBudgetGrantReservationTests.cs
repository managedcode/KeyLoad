namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class ReadExecutionBudgetGrantReservationTests
{
    private const long CallerBytes = 64;
    private const int CallerRecords = 10_000;

    private const string ExaminedExceededDiagnostic = "The read execution examined-record budget is exceeded.";
    [Test]
    public async Task ExactLeafGrantsShareOneAggregateByteLedger()
    {
        var maximumBytes = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length
            + ReadExecutionBudgetGrantSeed.SecondKey.Length + ReadExecutionBudgetGrantSeed.SecondValue.Length;
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase(new()
        {
        });
        var result = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget();
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
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var rootCharge = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(_ =>
        {
            var budget = new ReadExecutionBudget();
            budget.ConstrainWork(CallerBytes, CallerRecords);
            budget.CreateReadGrant(64, budget.RemainingReadGrantRecords);
            budget.ChargeBytes(1);
            return true;
        }));
        var additionalGrant = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(_ =>
        {
            var budget = new ReadExecutionBudget();
            budget.ConstrainWork(CallerBytes, CallerRecords);
            budget.CreateReadGrant(64, budget.RemainingReadGrantRecords);
            budget.CreateReadGrant(1, 0);
            return true;
        }));
        var recordGrant = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(_ =>
        {
            var budget = new ReadExecutionBudget();
            budget.ConstrainWork(CallerBytes, CallerRecords);
            budget.CreateReadGrant(0, budget.RemainingReadGrantRecords);
            budget.CreateReadGrant(0, 1);
            return true;
        }));

        await Assert.That(rootCharge.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(additionalGrant.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(recordGrant.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(recordGrant.Message).IsEqualTo(ExaminedExceededDiagnostic);
    }
}
