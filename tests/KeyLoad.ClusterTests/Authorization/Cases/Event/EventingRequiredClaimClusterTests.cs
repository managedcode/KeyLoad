namespace KeyLoad.ClusterTests.Authorization;

[NotInParallel]
internal sealed class EventingRequiredClaimClusterTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task RequiredBodyOrHeaderBeforeClaimRemainsPrivateAcrossActualSdkMcpRepairAndTwoColdOwners(bool header)
        => EventingRequiredClaimClusterFlow.RunAsync(header);
}
