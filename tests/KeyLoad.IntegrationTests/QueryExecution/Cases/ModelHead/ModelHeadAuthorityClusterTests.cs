namespace KeyLoad.IntegrationTests.QueryExecution;

[NotInParallel]
internal sealed class ModelHeadAuthorityClusterTests
{
    [Test]
    public Task SupportedModelReadersPreserveTypedSourceAuthorityNonClaimPrivacyStaleRepairReplayAndTwoColdOwners()
        => ModelHeadClusterFlow.RunAsync();
}
