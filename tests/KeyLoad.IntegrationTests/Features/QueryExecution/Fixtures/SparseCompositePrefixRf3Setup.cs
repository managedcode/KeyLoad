using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SparseCompositePrefixRf3Setup
{
    internal static async Task<SparseCompositePrefixRf3State> RunAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers root, bool includeNull, bool includeMissing, CancellationToken token)
    {
        var partition = new PartitionRef("sparse-prefix-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        foreach (var fallback in new[] { false, true })
        {
            var definition = new ResourceDefinition(fallback ? SparseCompositePrefixRf3Data.Fallback : SparseCompositePrefixRf3Data.Only,
                ResourceKind.Collection, partition.TransactionDomainId)
            {
                Indexes = SparseCompositePrefixRf3Data.Indexes(includeNull, includeMissing, fallback),
                FieldPolicies = [new("/status", "restricted", SparseCompositePrefixRf3Data.ReadGrant, SparseCompositePrefixRf3Data.UseGrant)]
            };
            await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
        }
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [.. new[] { SparseCompositePrefixRf3Data.Only, SparseCompositePrefixRf3Data.Fallback }.SelectMany(collection =>
                SparseCompositePrefixRf3Data.Rows.Select(row => (Mutation)new PutDocument(collection, row.Id, SparseCompositePrefixRf3Requests.SeedJson(row.Id, row.Json))))]);
        var owner = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(1, partition), token));
        await SqlRf3Protocol.EqualAsync(partition, owner.Partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(command.CommandId,
            new(owner.Incarnation, partition.AtomicPartitionId, receipt.Token.Position, owner.PlacementEpoch),
            [.. command.Mutations.Cast<PutDocument>().Select(row => new MutationReceipt("putDocument", row.Resource, row.Id, 1L))],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, receipt);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, SparseCompositePrefixRf3Data.Only,
            Capability.Query | Capability.DocumentsRead, token);
        var state = new SparseCompositePrefixRf3State(partition, includeNull, includeMissing, command, receipt, identity,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token));
        await PolicyAsync(root, state, true, 2L, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, SparseCompositePrefixRf3State state,
        bool use, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(state.Partition.DatabaseId, SparseCompositePrefixRf3Data.Only, Capability.Query | Capability.DocumentsRead),
                new(state.Partition.DatabaseId, SparseCompositePrefixRf3Data.Fallback, Capability.Query | Capability.DocumentsRead)],
            FieldGrants = use ? [SparseCompositePrefixRf3Data.ReadGrant, SparseCompositePrefixRf3Data.UseGrant]
                : [SparseCompositePrefixRf3Data.ReadGrant]
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
