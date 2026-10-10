namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueLifecyclePhaseOneTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task FullDeadLetterPendingAtomicRefusalRedriveCancellationAndSameRootCold(bool byteSublimit)
        => QueueLifecycleTrial.RunAsync(byteSublimit, TestContext.Current!.Execution.CancellationToken);
}
