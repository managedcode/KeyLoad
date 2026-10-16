using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionRf3Protocol;
using S = KeyLoad.IntegrationTests.Features.Authorization.SafeProjectorOperationRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var suffix = Guid.NewGuid().ToString(S.GuidFormat);
        var partition = new PartitionRef(S.TenantPrefix + suffix, S.Database, S.TransactionDomain, suffix);
        var history = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var state = new SafeProjectorOperationRf3History(await SafeProjectorOperationRf3Seed.CreateAsync(root, partition, deadline.Token));
            await NativeSubmissionColdAssertions.ReceiptAsync(state.State.SeedReceipt,
                new CommitReceipt(state.State.Seed.CommandId, state.State.SeedReceipt.Token,
                [
                    new(S.PutKind, state.State.Collection, S.First, S.InitialRevision),
                    new(S.PutKind, state.State.Collection, S.Second, S.InitialRevision)
                ], DurabilityProfile.QuorumProcessDurable));
            await SchemaVisibleProjectionRf3Joins.ConfigureAsync(root, state, deadline.Token);
            await SchemaVisibleProjectionRf3Authority.InvalidAsync(root, state, deadline.Token);
            var resource = await SchemaVisibleProjectionRf3Authority.ResourceAsync(root, state, state.State.Resource, false, deadline.Token);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.State.Key, async reader =>
            {
                await WarmAsync(root, reader, state, resource, deadline.Token);
                return true;
            }, deadline.Token);
            return state;
        }, deadline.Token);
        await SchemaVisibleProjectionRf3Cold.RunAsync(fixture, history, deadline.Token);
    }

    private static async Task WarmAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, ResourceDefinition resource, CancellationToken token)
    {
        await PhaseAsync(root, reader, history, P.MaskedEpoch, false, false, token);
        await PhaseAsync(root, reader, history, P.RawEpoch, true, false, token);
        await PhaseAsync(root, reader, history, P.SchemaEpoch, true, true, token);
        await SafeProjectorOperationRf3Mutations.ProtectedRefusedAsync(root, reader, history, P.SchemaEpoch, token);
        await PhaseAsync(root, reader, history, P.RevokedEpoch, true, false, token);
        var mixed = await SchemaVisibleProjectionRf3Authority.ResourceAsync(root, history, resource, true, token);
        await SchemaVisibleProjectionRf3Authority.PrincipalAsync(root, history, P.FinalEpoch, false, false, token);
        await SchemaVisibleProjectionRf3Values.WaveAsync(root, reader, history, false, true, token);
        await SchemaVisibleProjectionRf3Bindings.RefusedAsync(root, reader, history, token);
        await SchemaVisibleProjectionRf3Replay.HealthyAsync(root, reader, history, token);
        await SchemaVisibleProjectionRf3Values.WaveAsync(root, reader, history, false, true, token);
        await SchemaVisibleProjectionRf3Bindings.RefusedAsync(root, reader, history, token);
        await SchemaVisibleProjectionRf3Root.ConfigureAsync(root, history, mixed, token);
        await SchemaVisibleProjectionRf3Root.WaveAsync(root, reader, history, token);
    }

    private static async Task PhaseAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, long epoch, bool raw, bool visible, CancellationToken token)
    {
        await SchemaVisibleProjectionRf3Authority.PrincipalAsync(root, history, epoch, raw, visible, token);
        await SchemaVisibleProjectionRf3Values.WaveAsync(root, reader, history, raw, false, token);
        if (visible)
        {
            await SafeProjectorOperationRf3Rows.WaveAsync(root, reader, history, true, token);
            await SafeProjectorOperationRf3Authority.UsageAsync(root, reader, history, true, true, token);
        }
        else
        {
            await SchemaVisibleProjectionRf3Bindings.RefusedAsync(root, reader, history, token);
        }
    }
}
