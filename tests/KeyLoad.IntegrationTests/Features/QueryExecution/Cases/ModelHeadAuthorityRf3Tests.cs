namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class ModelHeadAuthorityRf3Tests
{
    [Test]
    public Task SupportedModelReadersPreserveTypedSourceAuthorityNonClaimPrivacyStaleRepairReplayAndTwoColdOwners()
        => ModelHeadRf3Flow.RunAsync();
}
