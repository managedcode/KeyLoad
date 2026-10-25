using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.ResourceExecution;
using static KeyLoad.ClusterTests.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class StrongReadSessionSetup
{
    internal static async Task<StrongReadSessionState> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, Domain);
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, Database, definition);
        await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(creationId, creation, token)));
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(Collection, SentinelId, SentinelJson)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(InvalidCut);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(receipt.Token.Incarnation, partition.AtomicPartitionId, receipt.Token.Position),
            [new(PutKind, Collection, SentinelId, CreatedRevision)], DurabilityProfile.ProcessDurable, 3), receipt);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection, Capability.DocumentsRead, token);
        var state = new StrongReadSessionState(partition, identity, [new(command, receipt)], outbox, null,
            ResourceCreationLifetime.Create(creationId, creation));
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
