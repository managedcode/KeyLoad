namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class AtomicTriadStagedWholeFlowTests
{
    [Test]
    public Task LateDuplicateRollsBackRepeatedTriadThenFreshPolicySixReceiptsAndIntermediateOutboxImagesSurviveTwoColdOwners()
        => AtomicTriadStagedFlow.RunAsync();
}
