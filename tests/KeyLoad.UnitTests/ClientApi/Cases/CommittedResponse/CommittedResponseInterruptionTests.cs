using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class CommittedResponseInterruptionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalCommittedWriteSurvivesInterruptedBodyThenExactReplayConflictAndHealthyRevision(bool callerCancellation)
        => ConnectionScenario.RunAsync(scenario => CommittedResponseFlow.RunAsync(scenario, callerCancellation));
}
