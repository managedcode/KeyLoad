namespace KeyLoad.IntegrationTests.Features.Messaging;

// Each case owns the real RF3 signed hold/settlement probe infrastructure exclusively.
[NotInParallel]
internal sealed class QueueWorkerOriginalUnknownRf3Tests
{
    [Test]
    public Task ActualCommittedWorkerAckInterruptionRetainsOriginalAndReconcilesWithoutHandlerRerun()
        => QueueWorkerRf3OriginalUnknown.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task ActualCommittedReceiveInterruptionStopsWithoutHandlerOrNewGuidAndReconcilesOriginal()
        => QueueWorkerRf3ReceiveUnknown.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task ActualFirstRenewalSignedProbeMustCaptureUnknownBeforeWorkerCanAcknowledge()
        => QueueWorkerRf3RenewalUnknown.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
