namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Owns its complete RF3 cold cohort; no shared caller fixture is stopped.</summary>
internal sealed class QueueLeaseFencingRf3Tests
{
    [Test]
    public Task ActualRenewAndNackReceiptsSurviveColdReclaimStaleTokensThenAcknowledgedHealthyWork()
        => QueueLeaseRf3Trial.RunAsync();
}
