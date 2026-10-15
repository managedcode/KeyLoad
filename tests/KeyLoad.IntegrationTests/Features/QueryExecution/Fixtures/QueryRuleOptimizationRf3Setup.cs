using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationRf3Setup
{
    private const int PlacementVersion = 1;
    private const long SeedRevision = 1L;
    private const long UpdateRevision = 2L;
    private const long InitialPolicyEpoch = 2L;

    private const long InitialOutboxRecords = 18L;
    internal static async Task<QueryRuleOptimizationRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        bool includeNull, bool includeMissing, CancellationToken token)
    {
        var partition = new PartitionRef("rule-optimization-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        for (var collection = 0; collection < QueryRuleOptimizationRf3Data.Collections.Length; collection++)
        {
            var definition = QueryRuleOptimizationRf3Data.Resource(partition, collection, includeNull, includeMissing);
            await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
                Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
        }
        var originals = new List<(CommandRequest Command, CommitReceipt Receipt)>();
        foreach (var command in new[] { QueryRuleOptimizationRf3Data.Seed(partition), QueryRuleOptimizationRf3Data.Update(partition) })
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
            var revision = originals.Count == 0 ? SeedRevision : UpdateRevision;
            var owner = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(PlacementVersion, partition), token));
            await Assert.That(actual.Token.Position).IsGreaterThan(0L);
            var expected = new CommitReceipt(command.CommandId,
                new(owner.Incarnation, partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch),
                [.. command.Mutations.Cast<PutDocument>().Select(row => new MutationReceipt("putDocument", row.Resource, row.Id, revision))],
                DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(expected, actual);
            originals.Add((command, actual));
        }
        await Assert.That(originals[1].Receipt.Token.Position).IsGreaterThan(originals[0].Receipt.Token.Position);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, partition, QueryRuleOptimizationRf3Data.Collections[0],
            Capability.Query | Capability.DocumentsRead, token);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        await Assert.That(outbox.Head.Tail).IsEqualTo(InitialOutboxRecords);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(InitialOutboxRecords);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(outbox.Consumers).IsEmpty();
        var state = new QueryRuleOptimizationRf3State(partition, includeNull, includeMissing, reader, outbox, originals.ToArray());
        await PolicyAsync(root, state, InitialPolicyEpoch, true, token);
        await QueryRuleOptimizationRf3Current.CheckAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, QueryRuleOptimizationRf3State state,
        long epoch, bool allow, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [.. QueryRuleOptimizationRf3Data.Collections.Select(collection => new ScopeGrant(state.Partition.DatabaseId,
                collection, Capability.Query | Capability.DocumentsRead))],
            FieldGrants = allow ? [QueryRuleOptimizationRf3Data.ReadGrant, QueryRuleOptimizationRf3Data.UseGrant] : [QueryRuleOptimizationRf3Data.ReadGrant]
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
