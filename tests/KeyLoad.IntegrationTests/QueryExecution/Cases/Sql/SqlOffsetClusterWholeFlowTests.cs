using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlOffsetClusterWholeFlowTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcSqlc006BoundedSdkOfficialMcpOffsetsContinueSignedCutsRepairPolicyAndReopenTwice()
    {
        using var deadline = McpCallerDeadline.Create();
        await SqlOffsetClusterFlow.RunAsync(fixture, deadline.Token);
        await QueryCursorRequestIdentityR47Flow.RunAsync(fixture, deadline.Token);
    }
}
