namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaAppliedOutcomeWholeFlowTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public Task NativeAppliedBlobRefusalOrFreshPolicyObservationPreservesOriginalOutcomeThenHealthyPublication(int scenario)
        => ReplicaAppliedOutcomeWholeFlow.RunAsync(scenario);
}
