using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "event-projection-lineage-r41"), NotInParallel]
internal sealed class EventProjectionLineageR41Tests(ClusterFixture fixture)
{
    [Test]
    public async Task DeletedSourceFencesDerivedProjectionAndExplicitOrdinaryVectorReplacementPreservesIndependentPagesAcrossReplayAndTwoColdCycles()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(() => EventProjectionLineageR41Trial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
