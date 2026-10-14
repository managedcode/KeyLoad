namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>AC-MSG-001: actual caller interruption after a committed ACK, then exact outcome recovery.</summary>
// Each case owns an exclusive heavy RF3 probe wave and its signed hold/settlement controls.
[NotInParallel]
internal sealed class QueueAckResponseLossRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task CommittedAckCallerInterruptionReconcilesOriginalReceiptWithoutSecondEffectThenHealthyDelivery(bool useMcp)
        => QueueAckResponseLossRf3Scenario.RunAsync(useMcp, TestContext.Current!.Execution.CancellationToken);
}
