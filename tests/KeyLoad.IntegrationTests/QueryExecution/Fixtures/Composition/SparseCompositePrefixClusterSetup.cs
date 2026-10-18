using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SparseCompositePrefixClusterSetup
{
    internal static async Task<SparseCompositePrefixClusterState> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, bool includeNull, bool includeMissing, CancellationToken token)
    {
        var partition = new PartitionRef("sparse-prefix-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        foreach (var includeStatusIndex in new[] { false, true })
        {
            var definition = new ResourceDefinition(includeStatusIndex ? SparseCompositePrefixClusterData.WithStatusIndex : SparseCompositePrefixClusterData.Only,
                ResourceKind.Collection, partition.TransactionDomainId)
            {
                Indexes = SparseCompositePrefixClusterData.Indexes(includeNull, includeMissing, includeStatusIndex),
                FieldPolicies = [new("/status", "restricted", SparseCompositePrefixClusterData.ReadGrant, SparseCompositePrefixClusterData.UseGrant)]
            };
            await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
        }
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [.. new[] { SparseCompositePrefixClusterData.Only, SparseCompositePrefixClusterData.WithStatusIndex }.SelectMany(collection =>
                SparseCompositePrefixClusterData.Rows.Select(row => (Mutation)new PutDocument(collection, row.Id, SparseCompositePrefixClusterRequests.SeedJson(row.Id, row.Json))))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(command.CommandId,
            new(receipt.Token.Incarnation, partition.AtomicPartitionId, receipt.Token.Position),
            [.. command.Mutations.Cast<PutDocument>().Select(row => new MutationReceipt("putDocument", row.Resource, row.Id, 1L))],
            DurabilityProfile.ProcessDurable, 3);
        await SqlClusterProtocol.EqualAsync(expected, receipt);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, SparseCompositePrefixClusterData.Only,
            Capability.Query | Capability.DocumentsRead, token);
        var state = new SparseCompositePrefixClusterState(partition, includeNull, includeMissing, command, receipt, identity,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token));
        await PolicyAsync(root, state, true, 2L, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, SparseCompositePrefixClusterState state,
        bool use, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(state.Partition.DatabaseId, SparseCompositePrefixClusterData.Only, Capability.Query | Capability.DocumentsRead),
                new(state.Partition.DatabaseId, SparseCompositePrefixClusterData.WithStatusIndex, Capability.Query | Capability.DocumentsRead)],
            FieldGrants = use ? [SparseCompositePrefixClusterData.ReadGrant, SparseCompositePrefixClusterData.UseGrant]
                : [SparseCompositePrefixClusterData.ReadGrant]
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
