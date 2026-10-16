namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsBoundReadBudgetTests
{
    [Test]
    public Task ActualEarlierReadBytesCannotBeResetByAnotherBoundRawRead()
        => AnalyticsBoundReadBudgetFlow.BytesAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ActualEarlierReadRecordsCannotBeResetByBoundAggregateCapture()
        => AnalyticsBoundReadBudgetFlow.RecordsAsync(TestContext.Current!.Execution.CancellationToken);
}
