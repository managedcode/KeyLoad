namespace KeyLoad.IntegrationTests.ResourceExecution;

[NotInParallel]
internal sealed class SlowBodyAdmissionReturnTests
{
    [Test]
    public Task CanceledAndDisconnectedSlowBodiesReturnCapacityThenBothCallersAndTwoCold()
        => SlowBodyAdmissionReturnFlow.RunAsync();
}
