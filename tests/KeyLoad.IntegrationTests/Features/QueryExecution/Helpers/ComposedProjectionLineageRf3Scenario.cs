using KeyLoad.IntegrationTests.Features.DatabaseComposition;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ComposedProjectionLineageRf3Scenario
{
    internal static async Task<QueueReferenceSelectState> StartAsync(ClusterFixture fixture, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var state = await QueueReferenceSelectSetup.RunAsync(fixture, admin, token);
            await ComposedProjectionLineageRf3Policy.ConfigureScoreAsync(admin, state, token);
            return state;
        }, token);

    internal static async Task PolicyAsync(ClusterFixture fixture, QueueReferenceSelectState state, long epoch,
        bool referenceUse, bool scoreUse, bool reads, CancellationToken token, bool scoreRead = true)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            await ComposedProjectionLineageRf3Policy.SetAsync(admin, state, epoch, referenceUse, scoreUse, reads, token, scoreRead);
            return true;
        }, token);

    internal static async Task<QueueReferenceSelectState> RepairAsync(ClusterFixture fixture, QueueReferenceSelectState state, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(QueueReferenceSelectProtocol.Target, "m", QueueReferenceSelectProtocol.RepairedJson,
                    Access: new(OwnerId: state.Identity.Principal.Id))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
            return state with { Repair = command, RepairReceipt = receipt };
        }, token);
}
