namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferAttemptColdTests
{
    [Test]
    public Task GenuineTargetCapacityFailureRequiresRepairBeforeBoundedAttemptAndTwoColdReceiptReplays()
        => RemoteTransferAttemptColdTrial.RunAsync();

    [Test]
    public Task GenuineCapacityWitnessCannotExceedRetainedCeilingThenFreshOperatorRepairSurvivesTwoColdCuts()
        => RemoteTransferAttemptColdTrial.RunAsync(RemoteTransferAttemptColdProtocol.SingleAttemptCeiling);
}
