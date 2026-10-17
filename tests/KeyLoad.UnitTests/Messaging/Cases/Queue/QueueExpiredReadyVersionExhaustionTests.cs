namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueExpiredReadyVersionExhaustionTests
{
    [Test]
    [Arguments(QueueClaimVersionExhaustion.StateVersion)]
    [Arguments(QueueClaimVersionExhaustion.Attempts)]
    [Arguments(QueueClaimVersionExhaustion.LeaseVersion)]
    public Task ExpiredReadyRequiresOnlyStateVersionSpaceAndRetainsOriginalOutcomeAcrossColdRepairAndHealthyWork(
        QueueClaimVersionExhaustion field)
        => QueueExpiredReadyVersionExhaustionTrial.RunAsync(field, TestContext.Current!.Execution.CancellationToken);
}
