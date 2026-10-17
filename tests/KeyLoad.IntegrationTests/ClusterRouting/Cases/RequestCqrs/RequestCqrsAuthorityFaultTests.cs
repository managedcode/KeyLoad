namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RequestCqrsAuthorityFaultTests
{
    [Test]
    public Task SdkWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects()
        => RequestCqrsAuthorityFaultScenario.RunAsync(officialMcp: false,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task OfficialMcpWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects()
        => RequestCqrsAuthorityFaultScenario.RunAsync(officialMcp: true,
            TestContext.Current!.Execution.CancellationToken);
}
