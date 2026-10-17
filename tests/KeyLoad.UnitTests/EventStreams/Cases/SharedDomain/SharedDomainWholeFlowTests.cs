namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class SharedDomainWholeFlowTests
{
    [Test]
    public async Task SameLiteralAndOriginalCommandAcrossFiveActiveScopesRetainNativeProducerConsumerAuthorityAndTwoColdOwners()
        => await SharedDomainWholeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
