namespace KeyLoad.IntegrationTests.QueryExecution;

[NotInParallel]
internal sealed class SqlParserAdmissionClusterWholeFlowTests
{
    [Test]
    public Task AnnotationsAndPrivateLexicalBoundsRefuseThenCanonicalSqlAstAndTypedInsertReplaySurviveTwoColdOwners()
        => SqlParserAdmissionClusterFlow.RunAsync();
}
