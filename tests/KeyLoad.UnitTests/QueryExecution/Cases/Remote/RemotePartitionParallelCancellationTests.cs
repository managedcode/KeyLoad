
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class RemotePartitionParallelCancellationTests
{
    [Test]
    public Task IndependentProducersReadActualOwnersBeforeCancellationAndJoinBeforeLiteralHealthyMerge()
        => QueryHostedParallelCancellation.RunAsync();
}
