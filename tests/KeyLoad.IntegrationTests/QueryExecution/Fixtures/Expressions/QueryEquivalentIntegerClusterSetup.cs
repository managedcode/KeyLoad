using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using static KeyLoad.IntegrationTests.QueryExecution.QueryEquivalentIntegerClusterDecimalProtocol;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryEquivalentIntegerClusterSetup
{
    private const string TenantPrefix = "integer-decimal-";
    private const string Domain = "domain";
    internal static async Task<QueryEquivalentIntegerClusterState> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, Domain);
        await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [.. Records.Select(row => (Mutation)new PutDocument(Collection, row.Id, row.Json))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(receipt.Token.Incarnation, partition.AtomicPartitionId, receipt.Token.Position),
            [.. Records.Select(row => new MutationReceipt(PutKind, Collection, row.Id, Revision))],
            DurabilityProfile.ProcessDurable, 3), receipt);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection,
            Capability.Query | Capability.DocumentsRead, token);
        var state = new QueryEquivalentIntegerClusterState(partition, command, receipt, identity, outbox);
        await QueryEquivalentIntegerClusterCurrent.ObserveAsync(root, state, () => Task.CompletedTask, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, QueryEquivalentIntegerClusterState state,
        long epoch, bool allowed, CancellationToken token)
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
