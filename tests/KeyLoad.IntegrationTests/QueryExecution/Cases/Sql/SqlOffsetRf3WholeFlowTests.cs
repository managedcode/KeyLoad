using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlOffsetRf3WholeFlowTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcSqlc006BoundedSdkOfficialMcpOffsetsContinueSignedCutsRepairPolicyAndReopenTwice()
    {
        using var deadline = McpCallerDeadline.Create();
        await SqlOffsetRf3Flow.RunAsync(fixture, deadline.Token);
        await QueryCursorRequestIdentityR47Flow.RunAsync(fixture, deadline.Token);
    }
}
