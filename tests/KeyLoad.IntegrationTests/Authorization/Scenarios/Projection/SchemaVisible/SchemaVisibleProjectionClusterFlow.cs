using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.StorageReplication;
using P = KeyLoad.IntegrationTests.Authorization.SchemaVisibleProjectionClusterProtocol;
using S = KeyLoad.IntegrationTests.Authorization.SafeProjectorOperationClusterProtocol;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class SchemaVisibleProjectionClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var suffix = Guid.NewGuid().ToString(S.GuidFormat);
        var partition = new PartitionRef(S.TenantPrefix + suffix, S.Database, S.TransactionDomain, suffix);
        var history = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var state = new SafeProjectorOperationClusterHistory(await SafeProjectorOperationClusterSeed.CreateAsync(root, partition, deadline.Token));
            await SubmissionColdAssertions.ReceiptAsync(state.State.SeedReceipt,
                new CommitReceipt(state.State.Seed.CommandId, state.State.SeedReceipt.Token,
                [
                    new(S.PutKind, state.State.Collection, S.First, S.InitialRevision),
                    new(S.PutKind, state.State.Collection, S.Second, S.InitialRevision)
                ], DurabilityProfile.ProcessDurable, 3));
            await SchemaVisibleProjectionClusterJoins.ConfigureAsync(root, state, deadline.Token);
            await SchemaVisibleProjectionClusterAuthority.InvalidAsync(root, state, deadline.Token);
            var resource = await SchemaVisibleProjectionClusterAuthority.ResourceAsync(root, state, state.State.Resource, false, deadline.Token);
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.State.Key, async reader =>
            {
                await WarmAsync(root, reader, state, resource, deadline.Token);
                return true;
            }, deadline.Token);
            return state;
        }, deadline.Token);
        await SchemaVisibleProjectionClusterCold.RunAsync(fixture, history, deadline.Token);
    }

    private static async Task WarmAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, ResourceDefinition resource, CancellationToken token)
    {
        await PhaseAsync(root, reader, history, P.MaskedEpoch, false, false, token);
        await PhaseAsync(root, reader, history, P.RawEpoch, true, false, token);
        await PhaseAsync(root, reader, history, P.SchemaEpoch, true, true, token);
        await SafeProjectorOperationClusterMutations.ProtectedRefusedAsync(root, reader, history, P.SchemaEpoch, token);
        await PhaseAsync(root, reader, history, P.RevokedEpoch, true, false, token);
        var mixed = await SchemaVisibleProjectionClusterAuthority.ResourceAsync(root, history, resource, true, token);
        await SchemaVisibleProjectionClusterAuthority.PrincipalAsync(root, history, P.FinalEpoch, false, false, token);
        await SchemaVisibleProjectionClusterValues.WaveAsync(root, reader, history, false, true, token);
        await SchemaVisibleProjectionClusterBindings.RefusedAsync(root, reader, history, token);
        await SchemaVisibleProjectionClusterReplay.HealthyAsync(root, reader, history, token);
        await SchemaVisibleProjectionClusterValues.WaveAsync(root, reader, history, false, true, token);
        await SchemaVisibleProjectionClusterBindings.RefusedAsync(root, reader, history, token);
        await SchemaVisibleProjectionClusterRoot.ConfigureAsync(root, history, mixed, token);
        await SchemaVisibleProjectionClusterRoot.WaveAsync(root, reader, history, token);
    }

    private static async Task PhaseAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, long epoch, bool raw, bool visible, CancellationToken token)
    {
        await SchemaVisibleProjectionClusterAuthority.PrincipalAsync(root, history, epoch, raw, visible, token);
        await SchemaVisibleProjectionClusterValues.WaveAsync(root, reader, history, raw, false, token);
        if (visible)
        {
            await SafeProjectorOperationClusterRows.WaveAsync(root, reader, history, true, token);
            await SafeProjectorOperationClusterAuthority.UsageAsync(root, reader, history, true, true, token);
        }
        else
        {
            await SchemaVisibleProjectionClusterBindings.RefusedAsync(root, reader, history, token);
        }
    }
}
