namespace KeyLoad.IntegrationTests.Features.Authorization;

[NotInParallel]
internal sealed class RequiredProcessingPolicyRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentRequiredBodyAndHeaderGrantsFenceQueueProcessingFourRoutesThenRepairAndTwoCold(bool header)
        => await RequiredProcessingPolicyRf3Flow.RunAsync(header);
}
