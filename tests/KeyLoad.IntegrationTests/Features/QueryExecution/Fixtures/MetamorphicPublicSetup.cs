using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class MetamorphicPublicSetup
{
    internal static async Task<MetamorphicPublicState> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        int seed, CancellationToken token)
    {
        var partition = new PartitionRef("metamorphic-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        var rows = MetamorphicPublicData.Rows(seed);
        var definition = new ResourceDefinition(MetamorphicPublicData.Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            Indexes = [new(MetamorphicPublicData.Index, ["/status"])],
            FieldPolicies = [new("/status", "protected-status", MetamorphicPublicData.ReadGrant, MetamorphicPublicData.UseGrant),
                new("/title", "private-title", "title.read", "title.use")]
        };
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [.. rows.Select(row => (Mutation)new PutDocument(MetamorphicPublicData.Collection, row.Id, row.Json))]);
        var owner = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        await SqlRf3Protocol.EqualAsync(partition, owner.Partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(owner.Incarnation, partition.AtomicPartitionId, receipt.Token.Position, owner.PlacementEpoch),
            [.. rows.Select(row => new MutationReceipt("putDocument", MetamorphicPublicData.Collection, row.Id, 1L))],
            DurabilityProfile.QuorumProcessDurable), receipt);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, MetamorphicPublicData.Collection,
            Capability.Query | Capability.DocumentsRead, token);
        var state = new MetamorphicPublicState(partition, rows, command, receipt, identity,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token));
        await PolicyAsync(root, state, false, 2L, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, MetamorphicPublicState state,
        bool use, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            FieldGrants = use ? [MetamorphicPublicData.ReadGrant, MetamorphicPublicData.UseGrant] : [MetamorphicPublicData.ReadGrant]
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
