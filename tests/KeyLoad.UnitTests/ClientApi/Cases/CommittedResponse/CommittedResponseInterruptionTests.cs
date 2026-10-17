using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class CommittedResponseInterruptionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalCommittedWriteSurvivesInterruptedBodyThenExactReplayConflictAndHealthyRevision(bool callerCancellation)
        => ConnectionNativeScenario.RunAsync(scenario => CommittedResponseFlow.RunAsync(scenario, callerCancellation));
}
