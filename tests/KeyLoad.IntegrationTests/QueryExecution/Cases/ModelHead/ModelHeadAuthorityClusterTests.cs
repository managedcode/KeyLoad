namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class ModelHeadAuthorityClusterTests
{
    [Test]
    public Task SupportedModelReadersPreserveTypedSourceAuthorityNonClaimPrivacyStaleRepairReplayAndTwoColdOwners()
        => ModelHeadClusterFlow.RunAsync();
}
