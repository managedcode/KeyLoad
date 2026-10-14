namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueClaimVersionExhaustionTests
{
    [Test]
    [Arguments(QueueClaimVersionExhaustion.Attempts)]
    [Arguments(QueueClaimVersionExhaustion.StateVersion)]
    [Arguments(QueueClaimVersionExhaustion.LeaseVersion)]
    public Task ExhaustedClaimRetainsOneBoundedFailureAndNoLeaseThenColdExactRowRepairRequiresNewIdentityAndHealthyWork(
        QueueClaimVersionExhaustion field)
        => QueueClaimVersionExhaustionTrial.RunAsync(field, TestContext.Current!.Execution.CancellationToken);
}
