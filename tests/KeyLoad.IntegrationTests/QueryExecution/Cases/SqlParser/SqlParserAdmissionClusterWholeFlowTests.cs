namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class SqlParserAdmissionClusterWholeFlowTests
{
    [Test]
    public Task AnnotationsAndPrivateLexicalBoundsRefuseThenCanonicalSqlAstAndTypedInsertReplaySurviveTwoColdOwners()
        => SqlParserAdmissionClusterFlow.RunAsync();
}
