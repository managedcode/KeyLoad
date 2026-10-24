namespace KeyLoad.UnitTests.EventStreams;

internal sealed class SharedDomainWholeFlowTests
{
    [Test]
    public async Task SameLiteralAndOriginalCommandAcrossFiveActiveScopesRetainProducerConsumerAuthorityAndTwoColdOwners()
        => await SharedDomainWholeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
