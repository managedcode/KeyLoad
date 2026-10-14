using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresCompositionRf3Provisioning
{
    internal static async Task<PostgresCompositionRf3Seed> CreateAsync(KeyLoadClient root, int dialect,
        CancellationToken token)
    {
        var partition = PostgresRf3State.Partition;
        foreach (var (name, kind) in new[]
        {
            (PostgresCompositionRf3Protocol.Graph, ResourceKind.Graph),
            (PostgresCompositionRf3Protocol.SourceQueue, ResourceKind.WorkQueue),
            (PostgresCompositionRf3Protocol.TargetQueue, ResourceKind.WorkQueue)
        })
        {
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(name, kind, partition.TransactionDomainId)), token));
        }
        var identity = await PostgresRf3Seed.IdentityAsync(root, IdentityMarker,
            Capability.GraphRead | Capability.Query, token, PostgresCompositionRf3Protocol.Graph);
        var principal = identity.Principal with
        {
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + EpochStep),
            Grants =
            [
                new(partition.DatabaseId, RelationalSqlRf3JoinBudgetData.LeftCollection, Capability.DocumentsRead | Capability.Query),
                new(partition.DatabaseId, RelationalSqlRf3JoinBudgetData.RightCollection, Capability.DocumentsRead | Capability.Query),
                new(partition.DatabaseId, PostgresCompositionRf3Protocol.SourceQueue, Capability.QueueInspect | Capability.Query),
                new(partition.DatabaseId, PostgresCompositionRf3Protocol.TargetQueue, Capability.QueueInspect | Capability.QueuePublish | Capability.Query),
                new(partition.DatabaseId, PostgresCompositionRf3Protocol.Graph, Capability.GraphRead | Capability.Query)
            ]
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await SqlRf3Protocol.EqualAsync(principal, persisted);
        identity = identity with
        {
            Principal = persisted
        };
        var payload = DatabaseCompositionRf3Payload.OrdinalGolden(new QueueGraphLink(PostgresRf3State.Left,
            PostgresRf3State.Right, PostgresCompositionRf3Protocol.Label, PostgresCompositionRf3Protocol.Attributes));
        var seed = new CommandRequest(Guid.NewGuid(), partition,
            [new EnqueueMessage(PostgresCompositionRf3Protocol.SourceQueue, PostgresCompositionRf3Protocol.Message, payload, HeadersJson: PostgresCompositionRf3Protocol.Attributes)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(seed, token));
        await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        var mutation = new QueueToGraph(PostgresCompositionRf3Protocol.Graph, PostgresCompositionRf3Protocol.SourceQueue,
            PostgresCompositionRf3Protocol.EdgePrefix, PostgresCompositionRf3Protocol.One,
            dialect == PostgresRf3Protocol.JoinDialect ? RelationalSqlRf3JoinBudgetData.LeftCollection : null,
            dialect == PostgresRf3Protocol.JoinDialect ? RelationalSqlRf3JoinBudgetData.RightCollection : null);
        var denied = new CommandRequest(Guid.NewGuid(), partition, [mutation]);
        return new(identity, persisted, seed, receipt, denied,
            await PostgresCompositionRf3Models.ReadAsync(root, token));
    }

    private const string IdentityMarker = "composition-";
    private const long EpochStep = 1;
}
