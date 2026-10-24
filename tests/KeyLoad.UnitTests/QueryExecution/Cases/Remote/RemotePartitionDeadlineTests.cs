
namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class RemotePartitionDeadlineTests
{
    [Test]
    public Task ActualReceivingSiloExceedsOriginalDeadlineThenJoinedProductionRequestsReturnLiteralHealthy()
        => QueryHostedSearchDeadline.RunAsync();
}
