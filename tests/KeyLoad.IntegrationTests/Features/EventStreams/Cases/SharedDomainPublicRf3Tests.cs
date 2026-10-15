namespace KeyLoad.IntegrationTests.Features.EventStreams;

[NotInParallel]
internal sealed class SharedDomainPublicRf3Tests
{
    [Test]
    public async Task AcSharedDomainPublicEqualKeyProducerProcessingRefusalsAndTwoRealRf3ColdOpens()
        => await SharedDomainPublicWholeFlow.RunAsync();
}
