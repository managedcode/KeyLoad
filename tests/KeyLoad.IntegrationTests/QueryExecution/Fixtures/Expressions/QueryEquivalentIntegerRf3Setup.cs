using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using static KeyLoad.IntegrationTests.Features.QueryExecution.QueryEquivalentIntegerRf3DecimalProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerRf3Setup
{
    private const string TenantPrefix = "integer-decimal-";
    private const string Domain = "domain";
    private const int PlacementVersion = 1;
    internal static async Task<QueryEquivalentIntegerRf3State> RunAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers root, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, Domain);
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [.. Records.Select(row => (Mutation)new PutDocument(Collection, row.Id, row.Json))]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, partition), token));
        await SqlRf3Protocol.EqualAsync(partition, placement.Partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(placement.Incarnation, partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [.. Records.Select(row => new MutationReceipt(PutKind, Collection, row.Id, Revision))],
            DurabilityProfile.QuorumProcessDurable), receipt);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection,
            Capability.Query | Capability.DocumentsRead, token);
        var state = new QueryEquivalentIntegerRf3State(partition, command, receipt, identity, outbox);
        await QueryEquivalentIntegerRf3Current.ObserveAsync(root, state, () => Task.CompletedTask, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, QueryEquivalentIntegerRf3State state,
        long epoch, bool allowed, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allowed ? state.Identity.Principal.Grants : []
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
