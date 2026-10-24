namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class ChunkWindowRecordCapacityWholeFlowTests
{
    [Test]
    public Task Actual257RecordCeilingRefuses258ThenFullExpiryDropFreshWindowRepairsReplaysAndTwoColdOwners()
        => ChunkWindowRecordCapacityFlow.RunAsync();
}
