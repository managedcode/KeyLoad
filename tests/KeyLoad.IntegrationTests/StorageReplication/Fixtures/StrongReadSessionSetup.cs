using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using static KeyLoad.IntegrationTests.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal static class StrongReadSessionSetup
{
    internal static async Task<StrongReadSessionState> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, Domain);
        await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, Database, definition), token)));
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(Collection, SentinelId, SentinelJson)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(InvalidCut);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(receipt.Token.Incarnation, partition.AtomicPartitionId, receipt.Token.Position),
            [new(PutKind, Collection, SentinelId, CreatedRevision)], DurabilityProfile.ProcessDurable, 3), receipt);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection, Capability.DocumentsRead, token);
        var state = new StrongReadSessionState(partition, identity, [new(command, receipt)], outbox, null);
        await StrongReadSessionCurrent.CheckAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, StrongReadSessionState state,
        bool allowed, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allowed ? state.Identity.Principal.Grants : []
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
