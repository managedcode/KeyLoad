namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedResourceLogSubscriberScopeFactory
{
    internal static IsolatedResourceLogSubscriberScope<T> Create<T>(
        IAsyncEnumerable<T> source, CancellationToken parentToken)
        => new(source, parentToken);
}
