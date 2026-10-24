namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SparseCompositePrefixWholeFlowTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task SparseCompositePrefixesKeepAllLiteralRowsPlansPermissionsReplayAndTwoCold(bool includeNull, bool includeMissing)
        => SparseCompositePrefixFlow.RunAsync(includeNull, includeMissing);
}
