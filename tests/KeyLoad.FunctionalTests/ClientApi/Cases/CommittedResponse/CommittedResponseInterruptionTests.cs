using KeyLoad.FunctionalTests.OrleansExecution;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class CommittedResponseInterruptionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalCommittedWriteSurvivesInterruptedBodyThenExactReplayConflictAndHealthyRevision(bool callerCancellation)
        => ConnectionScenario.RunAsync(scenario => CommittedResponseFlow.RunAsync(scenario, callerCancellation));
}
