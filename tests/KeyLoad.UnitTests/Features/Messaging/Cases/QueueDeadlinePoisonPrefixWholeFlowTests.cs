namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueDeadlinePoisonPrefixWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OversizedDeadlinePrefixRetainsOriginalThenOtherFamilyAdvancesExactRepairAndTwoColdReplays(bool leased)
        => QueueDeadlinePoisonPrefixFlow.RunAsync(leased, TestContext.Current!.Execution.CancellationToken);
}
