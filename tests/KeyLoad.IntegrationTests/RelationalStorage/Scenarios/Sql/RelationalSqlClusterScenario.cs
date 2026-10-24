using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.RelationalStorage;

/// <summary>One canonical atomic partition with typed rows and their existing linked models.</summary>
internal sealed record RelationalSqlClusterScenario(PartitionRef Partition, ResourceDefinition TableDefinition)
{
    internal EntityRef First => new(Partition, RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId);
    internal EntityRef Second => new(Partition, RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.SecondId);
    internal StreamRef Stream => new(Partition, RelationalSqlClusterTokens.Streams, RelationalSqlClusterTokens.StreamId);
    private static VectorSpace Space => new(RelationalSqlClusterTokens.VectorSpace, RelationalSqlClusterTokens.Dimension,
        DistanceMetric.DotProduct, RelationalSqlClusterTokens.VectorModel, RelationalSqlClusterTokens.VectorVersion);
    internal static async Task<RelationalSqlClusterScenario> CreateAsync(KeyLoadClient administrator, CancellationToken cancellationToken)
        => await CreateAsync(administrator, new PartitionRef(RelationalSqlClusterTokens.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            RelationalSqlClusterTokens.Database, RelationalSqlClusterTokens.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat)), cancellationToken);
    internal static async Task<RelationalSqlClusterScenario> CreateAsync(KeyLoadClient administrator,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var table = new ResourceDefinition(RelationalSqlClusterTokens.Table, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(RelationalSqlClusterTokens.Key, RelationalSqlClusterTokens.Columns),
            Indexes = [new(RelationalSqlClusterTokens.TitleIndex, [RelationalSqlClusterTokens.TitlePath], Unique: true)]
        };
        var persisted = await ConfigureAsync(administrator, partition, table, cancellationToken);
        await Assert.That(persisted.RelationalSchema!.PrimaryKey).IsEqualTo(RelationalSqlClusterTokens.Key);
        await SqlClusterProtocol.EqualAsync(RelationalSqlClusterTokens.Columns, persisted.RelationalSchema.Columns);
        await Assert.That(persisted.Indexes[0].Unique).IsTrue();
        foreach (var (name, kind) in new[]
        {
            (RelationalSqlClusterTokens.Documents, ResourceKind.Collection), (RelationalSqlClusterTokens.Streams, ResourceKind.StreamSet),
            (RelationalSqlClusterTokens.Queue, ResourceKind.WorkQueue), (RelationalSqlClusterTokens.Graph, ResourceKind.Graph),
            (RelationalSqlClusterTokens.SeriesSet, ResourceKind.TimeSeries), (RelationalSqlClusterTokens.Blobs, ResourceKind.BlobStore)
        })
        {
            await ConfigureAsync(administrator, partition, new(name, kind, partition.TransactionDomainId), cancellationToken);
        }
        return new(partition, persisted);
    }
    internal CommandRequest Command(params Mutation[] effects)
        => new(Guid.NewGuid(), Partition, [.. effects]);
    internal CommandRequest LinkedModelsCommand() => Command(
        new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.FirstRow),
        new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.SecondId, RelationalSqlClusterTokens.SecondRow),
        new PutDocument(RelationalSqlClusterTokens.Documents, RelationalSqlClusterTokens.DocumentId, RelationalSqlClusterTokens.EmptyJson),
        new UpsertEdge(RelationalSqlClusterTokens.Graph, RelationalSqlClusterTokens.EdgeId, First, Second, RelationalSqlClusterTokens.EdgeLabel),
        new PutVector(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.VectorField,
            [RelationalSqlClusterTokens.UnitVector, RelationalSqlClusterTokens.ZeroVector], Space, RelationalSqlClusterTokens.FirstRevision),
        new AppendEvents(RelationalSqlClusterTokens.Streams, RelationalSqlClusterTokens.StreamId,
            [new(RelationalSqlClusterTokens.EventId, RelationalSqlClusterTokens.EventType, RelationalSqlClusterTokens.EmptyJson)], ExpectedStreamRevision.NoStream),
        new EnqueueMessage(RelationalSqlClusterTokens.Queue, RelationalSqlClusterTokens.MessageId, RelationalSqlClusterTokens.EmptyJson),
        new AppendSamples(RelationalSqlClusterTokens.SeriesSet, RelationalSqlClusterTokens.SeriesId,
            [new(RelationalSqlClusterTokens.SampleId, RelationalSqlClusterTokens.SampleAt, RelationalSqlClusterTokens.SampleValue)]));
    internal SearchRequest Search() => new(Partition, RelationalSqlClusterTokens.Table, VectorField: RelationalSqlClusterTokens.VectorField,
        Vector: [RelationalSqlClusterTokens.UnitVector, RelationalSqlClusterTokens.ZeroVector], Space: Space);
    internal InspectMessageRequest Inspect(string id = RelationalSqlClusterTokens.MessageId)
        => new(new(Partition, RelationalSqlClusterTokens.Queue), id);
    internal ReadStreamRequest ReadStream(string id = RelationalSqlClusterTokens.StreamId)
        => new(Stream with
        {
            StreamId = id
        });
    private static Task<ResourceDefinition> ConfigureAsync(KeyLoadClient administrator, PartitionRef partition,
        ResourceDefinition definition, CancellationToken cancellationToken)
        => SqlClusterProtocol.SdkAsync<ResourceDefinition>(administrator, SqlClusterProtocol.Call(partition,
            McpCallerTools.ResourcesConfigure, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition),
            Guid.NewGuid()), cancellationToken);
}
