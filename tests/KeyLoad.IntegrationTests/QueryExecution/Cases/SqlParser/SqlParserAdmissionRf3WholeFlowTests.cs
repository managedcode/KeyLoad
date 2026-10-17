namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class SqlParserAdmissionRf3WholeFlowTests
{
    [Test]
    public Task NativeAnnotationsAndPrivateLexicalBoundsRefuseThenCanonicalSqlAstAndTypedInsertReplaySurviveTwoColdOwners()
        => SqlParserAdmissionRf3Flow.RunAsync();
}
