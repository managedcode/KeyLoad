using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueParkedPolicyRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task NonemptyProfileRefusalThenEmptyCasChangesOnlyFreshKeyedDeliveryAcrossTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await QueueParkedPolicyRf3Flow.RunAsync(fixture, deadline.Token);
    }
}
