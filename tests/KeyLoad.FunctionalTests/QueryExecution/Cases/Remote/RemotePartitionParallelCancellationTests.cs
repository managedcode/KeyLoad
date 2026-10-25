
namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class RemotePartitionParallelCancellationTests
{
    [Test]
    public Task IndependentProducersReadActualOwnersBeforeCancellationAndJoinBeforeLiteralHealthyMerge()
        => QueryHostedParallelCancellation.RunAsync();
}
