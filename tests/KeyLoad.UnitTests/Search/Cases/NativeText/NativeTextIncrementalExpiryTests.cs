using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>AC-FTS-INCREMENTAL-005: original signed page naturally expires, remains unacknowledged and permits genuine fresh-owner cleanup.</summary>
internal sealed class NativeTextIncrementalExpiryTests
{
    [Test]
    public async Task OriginalUnacknowledgedPageNaturallyExpiresWithoutRenewalThenFreshConsumerBuildReturnsLiteralHealthy()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(nativeReplicaAdmission: true);
            await ServerFailureObserver.ObserveAsync(() => NativeTextIncrementalExpiryTrial.RunAsync(database,
                TestContext.Current!.Execution.CancellationToken), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
