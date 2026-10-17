using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkMultiblockReadInterruptionFaults
{
    private const string DeadlineDetail = "The read execution deadline is exceeded.";

    internal static async Task CancellationAsync(ChunkSelectedIntegrityFixture fixture,
        ReadExecutionBudget budget, CancellationToken originalToken)
    {
        SampleRecord[]? result = null;
        var refusal = Assert.ThrowsExactly<OperationCanceledException>(() =>
        {
            result = ChunkMultiblockReadBudgetCalls.Read(fixture, budget);
        });
        await Assert.That(refusal.CancellationToken).IsEqualTo(originalToken);
        await Assert.That(result).IsNull();
    }

    internal static async Task DeadlineAsync(ChunkSelectedIntegrityFixture fixture, ReadExecutionBudget budget)
    {
        SampleRecord[]? result = null;
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            result = ChunkMultiblockReadBudgetCalls.Read(fixture, budget);
        });
        await Assert.That(refusal.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(refusal.Message).IsEqualTo(DeadlineDetail);
        await Assert.That(refusal.InnerException).IsNull();
        await Assert.That(result).IsNull();
    }
}
