namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class AtomicTriadOutboxTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public Task LateOutboxRefusalRollsBackEveryMixedModelThenOriginalFailureAndFreshWholeReceiptSurviveTwoColdOwners(int rotation)
        => AtomicTriadOutboxFlow.RunAsync(rotation);
}
