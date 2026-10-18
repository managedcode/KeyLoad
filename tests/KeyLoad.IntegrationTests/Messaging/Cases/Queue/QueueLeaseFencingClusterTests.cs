namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Owns its complete CLUSTER cold cohort; no shared caller fixture is stopped.</summary>
internal sealed class QueueLeaseFencingClusterTests
{
    [Test]
    public Task ActualRenewAndNackReceiptsSurviveColdReclaimStaleTokensThenAcknowledgedHealthyWork()
        => QueueLeaseClusterTrial.RunAsync();

    [Test]
    public Task OriginalDeadlineCasCannotExpireRenewedLeaseAcrossBothColdCutsThenCurrentTokenAckAndHealthyWork()
        => QueueRenewedDeadlinePublicTrial.RunAsync();
}
