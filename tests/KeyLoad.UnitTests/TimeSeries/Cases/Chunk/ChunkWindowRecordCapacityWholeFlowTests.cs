namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class ChunkWindowRecordCapacityWholeFlowTests
{
    [Test]
    public Task Actual257RecordCeilingRefuses258ThenFullExpiryDropFreshWindowRepairsReplaysAndTwoColdOwners()
        => ChunkWindowRecordCapacityFlow.RunAsync();
}
