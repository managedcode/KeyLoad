namespace KeyLoad.IntegrationTests.Features.Messaging;

[NotInParallel]
internal sealed class AtomicTriadStagedRf3Tests
{
    [Test]
    public Task RepeatedTriadLateDuplicateThenSixReceiptsCurrentPrincipalAndTwoColdOwners()
        => AtomicTriadStagedRf3Flow.RunAsync();
}
