namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class QueryAdapterTests
{
    [Test]
    public Task SqlJsonAndCSharpAdaptersHaveTheSameSeededResultsAndAccessPath()
        => QueryAdapterIndexedFlow.RunAsync();
}
