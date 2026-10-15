namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class AtomicTriadStagedWholeFlowTests
{
    [Test]
    public Task LateDuplicateRollsBackRepeatedTriadThenFreshPolicySixReceiptsAndIntermediateOutboxImagesSurviveTwoColdOwners()
        => AtomicTriadStagedFlow.RunAsync();
}
