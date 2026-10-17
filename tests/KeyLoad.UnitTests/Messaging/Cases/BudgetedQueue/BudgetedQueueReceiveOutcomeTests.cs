namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class BudgetedQueueReceiveOutcomeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeQueueReceiveOutcomeUsesOriginalBudgetAndFreshResourcePolicyCas(bool header)
        => await BudgetedQueueReceiveOutcomeFlow.RunAsync(header,
            TestContext.Current!.Execution.CancellationToken);
}
