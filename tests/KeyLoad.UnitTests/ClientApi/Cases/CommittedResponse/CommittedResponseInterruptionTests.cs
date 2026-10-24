using KeyLoad.UnitTests.OrleansExecution;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed class CommittedResponseInterruptionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalCommittedWriteSurvivesInterruptedBodyThenExactReplayConflictAndHealthyRevision(bool callerCancellation)
        => ConnectionScenario.RunAsync(scenario => CommittedResponseFlow.RunAsync(scenario, callerCancellation));
}
