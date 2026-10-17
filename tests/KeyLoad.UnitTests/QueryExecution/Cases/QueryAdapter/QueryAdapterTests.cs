namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryAdapterTests
{
    [Test]
    public Task SqlJsonAndCSharpAdaptersHaveTheSameSeededResultsAndAccessPath()
        => QueryAdapterIndexedFlow.RunAsync();
}
