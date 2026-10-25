using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

/// <summary>AC-FTS-INCREMENTAL-005: original signed page naturally expires, remains unacknowledged and permits genuine fresh-owner cleanup.</summary>
internal sealed class TextIncrementalExpiryTests
{
    [Test]
    public async Task OriginalUnacknowledgedPageNaturallyExpiresWithoutRenewalThenFreshConsumerBuildReturnsLiteralHealthy()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => TextIncrementalExpiryTrial.RunAsync(database,
                TestContext.Current!.Execution.CancellationToken), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
