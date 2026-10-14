using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventReferenceSelectRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcKl095EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            admin => EventReferenceSelectSetup.RunAsync(fixture, admin, token), token);
        await EventReferenceSelectFlow.ReaderAsync(fixture, state, denied: true, masked: false, token);
        await PolicyAsync(state, 3, allow: true, names: false, token);
        await EventReferenceSelectFlow.ReaderAsync(fixture, state, denied: false, masked: true, token);
        await ModelSqlRf3Cold.RunAsync(fixture, token);
        await EventReferenceSelectFlow.ReaderAsync(fixture, state, denied: false, masked: true, token);
        await PolicyAsync(state, 4, allow: false, names: false, token);
        await EventReferenceSelectFlow.ReaderAsync(fixture, state, denied: true, masked: false, token);
        await PolicyAsync(state, 5, allow: true, names: true, token);
        state = await RepairMissingAsync(state, token);
        await EventReferenceSelectFlow.ReaderAsync(fixture, state, denied: false, masked: false, token);
        await ModelSqlRf3Cold.RunAsync(fixture, token);
        await EventReferenceSelectFlow.ReaderAsync(fixture, state, denied: false, masked: false, token);
    }

    private async Task PolicyAsync(EventReferenceSelectState state, long epoch, bool allow, bool names, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => { await EventReferenceSelectSetup.PolicyAsync(admin, state, epoch, allow, names, token); return true; }, token);

    private async Task<EventReferenceSelectState> RepairMissingAsync(EventReferenceSelectState state, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(EventReferenceSelectProtocol.Target, "m", EventReferenceSelectProtocol.RepairedJson,
                    Access: new(OwnerId: state.Identity.Principal.Id))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
            return state with
            {
                Repair = command,
                RepairReceipt = receipt
            };
        }, token);
}
