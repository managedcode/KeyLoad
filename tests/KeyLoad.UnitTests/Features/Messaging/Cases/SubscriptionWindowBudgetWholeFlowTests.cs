namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SubscriptionWindowBudgetWholeFlowTests
{
    [Test]
    [Arguments(SubscriptionWindowBudgetFault.AggregateRecords)]
    [Arguments(SubscriptionWindowBudgetFault.AggregateBytes)]
    public Task CachedClaimAuthorityUsesOneTotalBudgetThenOriginalReceiptColdAndHealthyProgress(
        SubscriptionWindowBudgetFault fault)
        => SubscriptionWindowBudgetWholeFlow.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
