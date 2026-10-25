namespace KeyLoad.ClusterTests.Authorization;

[NotInParallel]
internal sealed class RequiredProcessingPolicyClusterTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentRequiredBodyAndHeaderGrantsFenceQueueProcessingFourRoutesThenRepairAndTwoCold(bool header)
        => await RequiredProcessingPolicyClusterFlow.RunAsync(header);
}
