using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadExecutionBudgetGrantPointTests
{
    private const string ExaminedExceededDiagnostic = "The read execution examined-record budget is exceeded.";

    [Test]
    public async Task ExactPointGrantChargesSharedBytesBeforeNativeReader()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var key = ReadExecutionBudgetGrantSeed.FirstKey;
        var maximumBytes = key.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length;
        var result = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(new());
            var grant = budget.CreateReadGrant(maximumBytes, 1);
            var bytesAtReader = 0L;
            var found = grant.ReadValue(view, key, _ => bytesAtReader = budget.ReadBytes);
            return (Found: found, RootBytes: budget.ReadBytes, BytesAtReader: bytesAtReader,
                GrantBytes: grant.ReadBytes, grant.ExaminedRecords);
        });

        await Assert.That(result.Found).IsTrue();
        await Assert.That(result.RootBytes).IsEqualTo(maximumBytes);
        await Assert.That(result.BytesAtReader).IsEqualTo(maximumBytes);
        await Assert.That(result.GrantBytes).IsEqualTo(maximumBytes);
        await Assert.That(result.ExaminedRecords).IsEqualTo(1);
    }

    [Test]
    public async Task MissingPointConsumesOneExaminedAttemptAndItsNativeKeyBytes()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var key = ReadExecutionBudgetGrantSeed.MissingKey;
        var budget = new ReadExecutionBudget(new());
        var grant = budget.CreateReadGrant(key.Length, 1);
        var found = database.Store.Read(view => grant.ReadValue(view, key, static _ => { }));

        await Assert.That(found).IsFalse();
        await Assert.That(grant.ReadBytes).IsEqualTo(key.Length);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(1);
        await Assert.That(budget.ReadBytes).IsEqualTo(key.Length);
    }

    [Test]
    public async Task ZeroRecordGrantRejectsMissingPointBeforeItsReader()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var budget = new ReadExecutionBudget(new());
        var grant = budget.CreateReadGrant(ReadExecutionBudgetGrantSeed.MissingKey.Length, 0);
        var consumed = false;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
            grant.ReadValue(view, ReadExecutionBudgetGrantSeed.MissingKey, _ => consumed = true)));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(error.Message).IsEqualTo(ExaminedExceededDiagnostic);
        await Assert.That(consumed).IsFalse();
        await Assert.That(budget.ReadBytes).IsEqualTo(0L);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(0);
    }

    [Test]
    public async Task OneByteOverPointGrantRejectsBeforeNativeReaderAndAggregateDebit()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var key = ReadExecutionBudgetGrantSeed.FirstKey;
        var maximumBytes = key.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length - 1;
        var budget = new ReadExecutionBudget(new());
        var grant = budget.CreateReadGrant(maximumBytes, 1);
        var consumed = false;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
            grant.ReadValue(view, key, _ => consumed = true)));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(consumed).IsFalse();
        await Assert.That(budget.ReadBytes).IsEqualTo(0L);
        await Assert.That(grant.ReadBytes).IsEqualTo(0L);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(0);
    }
}
