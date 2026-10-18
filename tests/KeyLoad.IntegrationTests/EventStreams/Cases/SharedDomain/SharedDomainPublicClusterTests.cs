namespace KeyLoad.IntegrationTests.Features.EventStreams;

[NotInParallel]
internal sealed class SharedDomainPublicClusterTests
{
    [Test]
    public async Task AcSharedDomainPublicEqualKeyProducerProcessingRefusalsAndTwoRealClusterColdOpens()
        => await SharedDomainPublicWholeFlow.RunAsync();
}
