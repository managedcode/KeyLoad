using System.Text.Json;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentCrudFixture
{
    internal const string Root = "root";
    internal const string Collection = "orders";
    internal const string DocumentId = "order-1";
    internal const string OtherDocumentId = "order-2";
    internal const string EmptyObject = "{}";
    internal const string LabelPath = "/label";
    internal const string IndexName = "by-label";
    internal const string SecretPath = "/secret";
    internal const string SecretIndexName = "by-secret";
    internal const string RawReadGrant = "secret.read";
    internal const string RawUseGrant = "secret.use";
    internal const string WriteGrant = "secret.write";

    internal static OperationResult Submit(TestDatabase database, params Mutation[] mutations)
        => Submit(database, Root, mutations);

    internal static OperationResult Submit(TestDatabase database, string principal, params Mutation[] mutations)
    {
        var commandId = Guid.NewGuid();
        return database.Submit(OperationKind.Batch,
            new CommandRequest(commandId, database.Partition, [.. mutations]), principal, commandId);
    }

    internal static OperationResult SubmitAt(TestDatabase database, string principal, PartitionRef partition,
        params Mutation[] mutations)
    {
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, partition, [.. mutations]);
        return database.Database.Apply(new(commandId, OperationKind.Batch, principal,
            TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options)));
    }

    internal static DocumentRecord? ReadRecord(TestDatabase database, EntityRef reference)
        => database.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)));

    internal static void ConfigureAuthority(TestDatabase database, string collection, DocumentAuthority authority)
    {
        var definition = new ResourceDefinition(collection, ResourceKind.Collection, database.Partition.TransactionDomainId)
        {
            Authority = authority
        };
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(
            database.Partition.TenantId, database.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
    }

    internal static void ConfigurePrincipal(TestDatabase database, string id, string resource,
        Capability capability, string[] fieldGrants, string? ownerId = null, bool restrictRows = false)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            new(id, database.Partition.TenantId, [new("database", resource, capability)], [.. fieldGrants])
            { OwnerId = ownerId, RestrictRows = restrictRows })).Get<PrincipalRecord>();

    internal static async Task AssertIndexedIds(TestDatabase database, string field, string indexName,
        string value, string[] expected, PartitionRef? partition = null)
    {
        var query = new QueryRequest(partition ?? database.Partition,
            $"SELECT * FROM {Collection} WHERE {field} = '{value}'");
        var page = new QueryEngine(database.Database).Execute(Root, query);
        await Assert.That(page.AccessPath).IsEqualTo("index:" + indexName);
        await Assert.That(page.Rows.Select(row => row.EntityId))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    internal static string NestedObjectJson(int objectCount)
        => string.Concat(Enumerable.Repeat("{\"x\":", objectCount)) + "0" +
            string.Concat(Enumerable.Repeat("}", objectCount));
}
