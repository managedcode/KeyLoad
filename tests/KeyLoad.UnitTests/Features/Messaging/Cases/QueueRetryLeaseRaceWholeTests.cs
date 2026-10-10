namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueRetryLeaseRaceWholeTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task RenewBeforeExpiryOrReclaimBeforeRenewRetainsFullStateAndColdContinuation(bool renewFirst)
        => QueueRetryLeaseRaceTrial.RunAsync(renewFirst, TestContext.Current!.Execution.CancellationToken);
}
