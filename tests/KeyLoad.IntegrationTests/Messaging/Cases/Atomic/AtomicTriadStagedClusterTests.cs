namespace KeyLoad.IntegrationTests.Messaging;

[NotInParallel]
internal sealed class AtomicTriadStagedClusterTests
{
    [Test]
    public Task RepeatedTriadLateDuplicateThenSixReceiptsCurrentPrincipalAndTwoColdOwners()
        => AtomicTriadStagedClusterFlow.RunAsync();
}
