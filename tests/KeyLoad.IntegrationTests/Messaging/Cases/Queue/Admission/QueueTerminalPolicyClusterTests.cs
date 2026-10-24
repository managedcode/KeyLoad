using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueTerminalPolicyClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task UnsupportedAttemptsChangeCannotRewriteRecordedRetryTerminalReceiptsAcrossTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await QueueTerminalPolicyClusterFlow.RunAsync(fixture, deadline.Token);
    }
}
