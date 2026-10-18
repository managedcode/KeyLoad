namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class SqlParserAdmissionClusterWholeFlowTests
{
    [Test]
    public Task NativeAnnotationsAndPrivateLexicalBoundsRefuseThenCanonicalSqlAstAndTypedInsertReplaySurviveTwoColdOwners()
        => SqlParserAdmissionClusterFlow.RunAsync();
}
