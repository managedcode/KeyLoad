
namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class RemotePartitionDeadlineTests
{
    [Test]
    public Task ActualReceivingSiloExceedsOriginalDeadlineThenJoinedProductionRequestsReturnLiteralHealthy()
        => QueryHostedSearchDeadline.RunAsync();
}
