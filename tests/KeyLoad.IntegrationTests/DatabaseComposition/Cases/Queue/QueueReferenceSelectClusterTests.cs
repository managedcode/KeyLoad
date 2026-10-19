using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class QueueReferenceSelectClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task QueueReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            admin => QueueReferenceSelectSetup.RunAsync(fixture, admin, token), token);
        await QueueReferenceSelectFlow.ReaderAsync(fixture, state, denied: true, masked: false, token);
        await PolicyAsync(state, 3, allow: true, names: false, token);
        await QueueReferenceSelectFlow.ReaderAsync(fixture, state, denied: false, masked: true, token);
        await ModelSqlClusterCold.RunAsync(fixture, token);
        await QueueReferenceSelectFlow.ReaderAsync(fixture, state, denied: false, masked: true, token);
        await PolicyAsync(state, 4, allow: false, names: false, token);
        await QueueReferenceSelectFlow.ReaderAsync(fixture, state, denied: true, masked: false, token);
        await PolicyAsync(state, 5, allow: true, names: true, token);
        state = await RepairMissingAsync(state, token);
        await QueueReferenceSelectFlow.ReaderAsync(fixture, state, denied: false, masked: false, token);
        await ModelSqlClusterCold.RunAsync(fixture, token);
        await QueueReferenceSelectFlow.ReaderAsync(fixture, state, denied: false, masked: false, token);
    }

    private async Task PolicyAsync(QueueReferenceSelectState state, long epoch, bool allow, bool names, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => { await QueueReferenceSelectSetup.PolicyAsync(admin, state, epoch, allow, names, token); return true; }, token);

    private async Task<QueueReferenceSelectState> RepairMissingAsync(QueueReferenceSelectState state, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(QueueReferenceSelectProtocol.Target, "m", QueueReferenceSelectProtocol.RepairedJson,
                    Access: new(OwnerId: state.Identity.Principal.Id))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
            return state with
            {
                Repair = command,
                RepairReceipt = receipt
            };
        }, token);
}
