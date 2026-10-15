namespace KeyLoad.IntegrationTests.Features.Authorization;

[NotInParallel]
internal sealed class EventingRequiredClaimRf3Tests
{
    [Test, Arguments(false), Arguments(true)]
    public Task RequiredBodyOrHeaderBeforeClaimRemainsPrivateAcrossActualSdkMcpRepairAndTwoColdOwners(bool header)
        => EventingRequiredClaimRf3Flow.RunAsync(header);
}
