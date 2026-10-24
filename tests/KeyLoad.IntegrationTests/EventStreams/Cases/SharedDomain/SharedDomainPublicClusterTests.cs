namespace KeyLoad.IntegrationTests.EventStreams;

[NotInParallel]
internal sealed class SharedDomainPublicClusterTests
{
    [Test]
    public async Task AcSharedDomainPublicEqualKeyProducerProcessingRefusalsAndTwoRealClusterColdOpens()
        => await SharedDomainPublicWholeFlow.RunAsync();
}
