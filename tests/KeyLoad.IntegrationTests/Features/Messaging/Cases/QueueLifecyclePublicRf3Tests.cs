using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueLifecyclePublicRf3Tests(ClusterFixture fixture)
{
    [Test, Arguments(0), Arguments(1), Arguments(2), Arguments(3)]
    public Task ParkedPendingAtomicRefusalCancelRedriveFreshLeaseFourRoutesAndSameOwnerCold(int route)
        => QueueLifecyclePublicTrial.RunAsync(fixture, route);
}
