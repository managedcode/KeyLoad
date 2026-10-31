namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockReadInterruptionFaults
{
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
}
