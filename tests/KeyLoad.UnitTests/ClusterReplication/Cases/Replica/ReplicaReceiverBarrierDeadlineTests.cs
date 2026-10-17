namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaReceiverBarrierDeadlineTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ReceiverReadRetainsReadDeadlineBeyondCommandDeadline(bool control)
        => ReplicaReceiverBarrierDeadlineFlow.RunAsync(control, TestContext.Current!.Execution.CancellationToken);
}
