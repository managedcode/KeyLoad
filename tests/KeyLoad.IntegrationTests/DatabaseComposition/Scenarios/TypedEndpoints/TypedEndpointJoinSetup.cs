using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class TypedEndpointJoinSetup
{
    private const string TenantPrefix = "typed-join-";
    private const string Database = "database";
    private const string Domain = "joined-models";
    private const string Key = "id";
    private const string Title = "title";
    private const string KeyPath = "/id";
    private const string Classification = "join-key";
    private const string ReadGrant = "join.pk.read";
    private const string WriteGrant = "join.pk.write";
    private const long EpochStep = 1;

    internal static async Task<TypedEndpointJoinSeed> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var from = Definition(partition, TypedEndpointJoinProtocol.FromCollection, TypedEndpointJoinProtocol.UseGrant);
        var to = Definition(partition, TypedEndpointJoinProtocol.ToCollection, TypedEndpointJoinProtocol.BlockedUseGrant);
        await ConfigureAsync(admin, partition, from, token);
        await ConfigureAsync(admin, partition, to, token);
        await ConfigureAsync(admin, partition,
            new(TypedEndpointJoinProtocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId), token);
        await ConfigureAsync(admin, partition,
            new(TypedEndpointJoinProtocol.EmptyQueue, ResourceKind.WorkQueue, partition.TransactionDomainId), token);
        await ConfigureAsync(admin, partition,
            new(TypedEndpointJoinProtocol.Graph, ResourceKind.Graph, partition.TransactionDomainId), token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition,
            TypedEndpointJoinProtocol.Graph, Capability.GraphRead | Capability.GraphWrite | Capability.Query, token);
        var principal = identity.Principal with
        {
            PolicyEpoch = identity.Principal.PolicyEpoch + EpochStep,
            OwnerId = identity.Principal.Id,
            RestrictRows = true,
            FieldGrants = [TypedEndpointJoinProtocol.UseGrant],
            Grants =
            [
                new(partition.DatabaseId, TypedEndpointJoinProtocol.FromCollection, Capability.DocumentsRead | Capability.Query),
                new(partition.DatabaseId, TypedEndpointJoinProtocol.ToCollection, Capability.DocumentsRead | Capability.Query),
                new(partition.DatabaseId, TypedEndpointJoinProtocol.Queue, Capability.QueueInspect | Capability.Query),
                new(partition.DatabaseId, TypedEndpointJoinProtocol.EmptyQueue, Capability.QueueInspect | Capability.Query),
                new(partition.DatabaseId, TypedEndpointJoinProtocol.Graph, Capability.GraphRead | Capability.GraphWrite | Capability.Query)
            ]
        };
        principal = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        var hidden = await McpPersistedIdentity.CreateAsync(fixture, partition,
            TypedEndpointJoinProtocol.FromCollection, Capability.DocumentsRead, token);
        var owner = new RowAccess(OwnerId: principal.Id);
        var first = TypedEndpointJoinProtocol.From(partition);
        var second = TypedEndpointJoinProtocol.To(partition);
        var seed = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(first.Collection, first.Id, TypedEndpointJoinProtocol.FirstJson, Access: owner),
            new PutDocument(second.Collection, second.Id, TypedEndpointJoinProtocol.SecondJson, Access: owner),
            new PutDocument(TypedEndpointJoinProtocol.FromCollection, TypedEndpointJoinProtocol.Hidden,
                TypedEndpointJoinProtocol.HiddenJson, Access: new(OwnerId: hidden.Principal.Id)),
            new EnqueueMessage(TypedEndpointJoinProtocol.Queue, TypedEndpointJoinProtocol.MatchedMessage,
                Payload(first, second)),
            new EnqueueMessage(TypedEndpointJoinProtocol.Queue, TypedEndpointJoinProtocol.MissingMessage,
                Payload(new(partition, TypedEndpointJoinProtocol.FromCollection, TypedEndpointJoinProtocol.Missing), second)),
            new EnqueueMessage(TypedEndpointJoinProtocol.Queue, TypedEndpointJoinProtocol.HiddenMessage,
                Payload(new(partition, TypedEndpointJoinProtocol.FromCollection, TypedEndpointJoinProtocol.Hidden), second))
        ]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(seed, token));
        await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(TypedEndpointJoinProtocol.SourceCount + TypedEndpointJoinProtocol.SourceCount);
        var models = await TypedEndpointJoinAssertions.CaptureAsync(admin, partition, token);
        await TypedEndpointJoinAssertions.LiteralAsync(models, partition, continued: false, joined: false);
        return new(partition, to, identity, principal, models);
    }

    internal static Task<ResourceDefinition> ConfigureAsync(RequestCqrsClusterCallers admin,
        PartitionRef partition, ResourceDefinition definition, CancellationToken token, long? expectedVersion = null)
        => ConfigureCoreAsync(admin, new(partition.TenantId, partition.DatabaseId, definition)
        {
            ExpectedSchemaVersion = expectedVersion
        }, token);

    private static async Task<ResourceDefinition> ConfigureCoreAsync(RequestCqrsClusterCallers admin,
        ConfigureResourceRequest request, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), request, token));

    internal static ResourceDefinition Definition(PartitionRef partition, string name, string useGrant)
        => new(name, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(Key, [new(Key, RelationalColumnType.Text), new(Title, RelationalColumnType.Text)]),
            FieldPolicies = [new(KeyPath, Classification, ReadGrant, useGrant, WriteGrant)]
        };

    private static string Payload(EntityRef from, EntityRef to)
        => JsonSerializer.Serialize(new QueueGraphLink(from, to, TypedEndpointJoinProtocol.Label,
            TypedEndpointJoinProtocol.Attributes), JsonDefaults.Options);
}
