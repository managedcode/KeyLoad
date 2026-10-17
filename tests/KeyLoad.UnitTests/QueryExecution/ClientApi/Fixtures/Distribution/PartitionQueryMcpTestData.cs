using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryMcpTestData
{
    internal const string Tenant = "mcp-query-tenant";
    internal const string Database = "mcp-query-database";
    internal const string Domain = "mcp-query-domain";
    internal const string PartitionKey = "mcp-query-partition";
    internal const string Collection = "mcp-query-records";
    internal const string EntityId = "mcp-query-entity";
    internal const string Json = "{\"name\":\"value\"}";
    internal const string AccessPath = "bounded-full-scan";
    private const string Root = "root";
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);

    internal static PartitionQueryRequestV1 Request() => new(1, [Partition],
        new SelectQuery(Collection, null, [new Selection("/name", "name")], null, [], 4),
        null, true, 1);

    internal static void Seed(TestDatabase fixture, PartitionQueryRequestV1 request,
        string entityId = EntityId)
    {
        var owner = PartitionQueryPublicTestSupport.ExpectedOwner;
        _ = fixture.Submit(OperationKind.BootstrapPhysicalShardCatalog,
            new BootstrapPhysicalShardCatalogRequest(1, 0, owner.PhysicalShardId, owner.Incarnation,
                owner.VoterIds)).Get<bool>();
        var partition = request.Partitions.Single();
        var resource = new ResourceDefinition(request.Query.Collection, ResourceKind.Collection,
            partition.TransactionDomainId);
        _ = fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource))
            .Get<ResourceDefinition>();
        var id = Guid.NewGuid();
        _ = fixture.Submit(OperationKind.Batch,
            new CommandRequest(id, partition, [new PutDocument(request.Query.Collection, entityId, Json)]),
            id: id).Get<CommitReceipt>();
    }

    internal static PartitionQueryRequestV1 Decode(PartitionQueryRequestV1 request)
    {
        if (!McpOperationCatalog.TryGet(McpCatalogExpectations.QueryPartitions, out var descriptor))
        {
            throw new InvalidOperationException("The partition query descriptor is missing.");
        }

        var payload = descriptor!.Decode(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options)
        }).Payload;
        return NativeSerialization.Deserialize<PartitionQueryRequestV1>(payload.Span);
    }

    internal static async Task<PartitionQueryPageV1> ExecuteAsync(TestDatabase fixture,
        PartitionQueryRequestV1 request, string entityId = EntityId)
    {
        var position = fixture.Store.Position;
        var page = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions(Root, request,
            PartitionQueryPublicTestSupport.ExpectedOwner);
        await VerifyPageAsync(fixture, request, page, position, entityId);
        return page;
    }

    internal static async Task VerifyPageAsync(TestDatabase fixture, PartitionQueryRequestV1 request,
        PartitionQueryPageV1 page, long position, string entityId = EntityId)
    {
        var partition = request.Partitions.Single();
        var reference = new EntityRef(partition, request.Query.Collection, entityId);
        var stored = fixture.Database.GetDocument(Root, reference)!;
        await Assert.That(page.Version).IsEqualTo(1);
        await Assert.That(page.Complete).IsTrue();
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        var row = page.Rows.Single();
        await Assert.That(row.Reference).IsEqualTo(reference);
        await Assert.That(row.Row.EntityId).IsEqualTo(entityId);
        await Assert.That(row.Row.Revision).IsEqualTo(stored.Revision);
        await Assert.That(row.Row.Revision).IsEqualTo(1);
        await Assert.That(row.Row.Json).IsEqualTo(Json);
        await Assert.That(row.Row.Redacted).IsFalse();
        await Assert.That(row.Row.RedactedFields!.Value.IsEmpty).IsTrue();
        await Assert.That(page.Leaves.Length).IsEqualTo(1);
        var leaf = page.Leaves.Single();
        await Assert.That(leaf.Partition).IsEqualTo(partition);
        await Assert.That(leaf.CutPosition).IsEqualTo(position);
        await Assert.That(leaf.PolicyEpoch).IsEqualTo(1);
        await Assert.That(leaf.SchemaVersion).IsEqualTo(1);
        await Assert.That(leaf.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(stored.Json).IsEqualTo(Json);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
    }
}
