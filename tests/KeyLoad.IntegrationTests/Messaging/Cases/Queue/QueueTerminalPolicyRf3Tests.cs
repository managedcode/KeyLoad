using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueTerminalPolicyRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task UnsupportedAttemptsChangeCannotRewriteRecordedRetryTerminalReceiptsAcrossTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await QueueTerminalPolicyRf3Flow.RunAsync(fixture, deadline.Token);
    }
}
