namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlParserAdmissionWholeFlowTests
{
    [Test]
    public Task MalformedPrivateInputAndNativeBoundsRefuseThenHealthyCurrentAuthorityReplayAndTwoColdOwners()
        => SqlParserAdmissionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
