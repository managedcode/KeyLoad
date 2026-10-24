using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueParkedPolicyClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task NonemptyProfileRefusalThenEmptyCasChangesOnlyFreshKeyedDeliveryAcrossTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await QueueParkedPolicyClusterFlow.RunAsync(fixture, deadline.Token);
    }
}
