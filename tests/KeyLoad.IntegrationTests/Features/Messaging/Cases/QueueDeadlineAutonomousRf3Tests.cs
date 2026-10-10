namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDeadlineAutonomousRf3Tests
{
    [Test]
    public Task ExistingScopedPrincipalRevokeColdRefusesDueThenRestoresAutonomousReadyAndOfficialClaimReceiptColdHealthy()
        => QueueDeadlineRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
