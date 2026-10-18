
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class RemotePartitionDeadlineTests
{
    [Test]
    public Task ActualReceivingSiloExceedsOriginalDeadlineThenJoinedProductionRequestsReturnLiteralHealthy()
        => QueryHostedSearchDeadline.RunAsync();
}
