using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class AdaptiveFilteredPublicRf3Tests
{
    [Test]
    public async Task OriginalThousandVectorCohortsRetainIndependentExactRecallPermissionsReceiptsAndTwoColdOwners()
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new DatabaseLimits(),
                new QueryExecutionOptions
                {
                    EnableApproximateSearch = true
                });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<AdaptiveFilteredPublicRf3Tests>(nameof(
                    OriginalThousandVectorCohortsRetainIndependentExactRecallPermissionsReceiptsAndTwoColdOwners));
                await AdaptiveFilteredPublicRf3Flow.RunAsync(fixture, deadline.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
