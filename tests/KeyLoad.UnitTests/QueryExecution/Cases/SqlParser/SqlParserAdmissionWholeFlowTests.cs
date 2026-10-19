namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlParserAdmissionWholeFlowTests
{
    [Test]
    public Task MalformedPrivateInputAndBoundsRefuseThenHealthyCurrentAuthorityReplayAndTwoColdOwners()
        => SqlParserAdmissionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
