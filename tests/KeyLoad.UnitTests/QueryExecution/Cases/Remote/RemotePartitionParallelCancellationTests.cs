
namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class RemotePartitionParallelCancellationTests
{
    [Test]
    public Task IndependentProducersReadActualOwnersBeforeCancellationAndJoinBeforeLiteralHealthyMerge()
        => QueryHostedParallelCancellation.RunAsync();
}
