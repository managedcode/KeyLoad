using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueOrderedRetryPublicRf3Tests(ClusterFixture fixture)
{
    [Test]
    [Arguments(0, QueueParkedHeadPolicy.Continue), Arguments(1, QueueParkedHeadPolicy.Continue)]
    [Arguments(2, QueueParkedHeadPolicy.Continue), Arguments(3, QueueParkedHeadPolicy.Continue)]
    [Arguments(0, QueueParkedHeadPolicy.Block), Arguments(1, QueueParkedHeadPolicy.Block)]
    [Arguments(2, QueueParkedHeadPolicy.Block), Arguments(3, QueueParkedHeadPolicy.Block)]
    public Task StrictHeadRecordedJitterParkDeniedRedriveOriginalReceiptsHealthyAndSameOwnerCold(int route, QueueParkedHeadPolicy parkedHead)
        => QueueOrderedRetryPublicTrial.RunAsync(fixture, route, parkedHead);
}
