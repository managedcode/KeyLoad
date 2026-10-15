namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class SlowBodyAdmissionReturnTests
{
    [Test]
    public Task CanceledAndDisconnectedSlowBodiesReturnCapacityThenBothCallersAndTwoCold()
        => SlowBodyAdmissionReturnFlow.RunAsync();
}
