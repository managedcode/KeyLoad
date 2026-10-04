using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>One canonical atomic partition with typed rows and their existing linked models.</summary>
internal sealed record RelationalSqlRf3Scenario(PartitionRef Partition, ResourceDefinition TableDefinition)
{
    internal EntityRef First => new(Partition, RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId);
    internal EntityRef Second => new(Partition, RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.SecondId);
    internal StreamRef Stream => new(Partition, RelationalSqlRf3Tokens.Streams, RelationalSqlRf3Tokens.StreamId);
    private static VectorSpace Space => new(RelationalSqlRf3Tokens.VectorSpace, RelationalSqlRf3Tokens.Dimension,
        DistanceMetric.DotProduct, RelationalSqlRf3Tokens.VectorModel, RelationalSqlRf3Tokens.VectorVersion);

    internal static async Task<RelationalSqlRf3Scenario> CreateAsync(KeyLoadClient administrator, CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(RelationalSqlRf3Tokens.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            RelationalSqlRf3Tokens.Database, RelationalSqlRf3Tokens.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var table = new ResourceDefinition(RelationalSqlRf3Tokens.Table, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(RelationalSqlRf3Tokens.Key, RelationalSqlRf3Tokens.Columns),
            Indexes = [new(RelationalSqlRf3Tokens.TitleIndex, [RelationalSqlRf3Tokens.TitlePath], Unique: true)]
        };
        var persisted = await ConfigureAsync(administrator, partition, table, cancellationToken);
        await Assert.That(persisted.RelationalSchema!.PrimaryKey).IsEqualTo(RelationalSqlRf3Tokens.Key);
        await SqlRf3Protocol.EqualAsync(RelationalSqlRf3Tokens.Columns, persisted.RelationalSchema.Columns);
        await Assert.That(persisted.Indexes[0].Unique).IsTrue();
        foreach (var (name, kind) in new[]
        {
            (RelationalSqlRf3Tokens.Documents, ResourceKind.Collection), (RelationalSqlRf3Tokens.Streams, ResourceKind.StreamSet),
            (RelationalSqlRf3Tokens.Queue, ResourceKind.WorkQueue), (RelationalSqlRf3Tokens.Graph, ResourceKind.Graph),
            (RelationalSqlRf3Tokens.SeriesSet, ResourceKind.TimeSeries), (RelationalSqlRf3Tokens.Blobs, ResourceKind.BlobStore)
        })
        {
            await ConfigureAsync(administrator, partition, new(name, kind, partition.TransactionDomainId), cancellationToken);
        }
        return new(partition, persisted);
    }

    internal CommandRequest Command(params Mutation[] effects)
        => new(Guid.NewGuid(), Partition, [.. effects]);

    internal CommandRequest LinkedModelsCommand() => Command(
        new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.FirstRow),
        new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.SecondId, RelationalSqlRf3Tokens.SecondRow),
        new PutDocument(RelationalSqlRf3Tokens.Documents, RelationalSqlRf3Tokens.DocumentId, RelationalSqlRf3Tokens.EmptyJson),
        new UpsertEdge(RelationalSqlRf3Tokens.Graph, RelationalSqlRf3Tokens.EdgeId, First, Second, RelationalSqlRf3Tokens.EdgeLabel),
        new PutVector(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.VectorField,
            [RelationalSqlRf3Tokens.UnitVector, RelationalSqlRf3Tokens.ZeroVector], Space, RelationalSqlRf3Tokens.FirstRevision),
        new AppendEvents(RelationalSqlRf3Tokens.Streams, RelationalSqlRf3Tokens.StreamId,
            [new(RelationalSqlRf3Tokens.EventId, RelationalSqlRf3Tokens.EventType, RelationalSqlRf3Tokens.EmptyJson)], ExpectedStreamRevision.NoStream),
        new EnqueueMessage(RelationalSqlRf3Tokens.Queue, RelationalSqlRf3Tokens.MessageId, RelationalSqlRf3Tokens.EmptyJson),
        new AppendSamples(RelationalSqlRf3Tokens.SeriesSet, RelationalSqlRf3Tokens.SeriesId,
            [new(RelationalSqlRf3Tokens.SampleId, RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleValue)]));

    internal SearchRequest Search() => new(Partition, RelationalSqlRf3Tokens.Table, VectorField: RelationalSqlRf3Tokens.VectorField,
        Vector: [RelationalSqlRf3Tokens.UnitVector, RelationalSqlRf3Tokens.ZeroVector], Space: Space);

    internal InspectMessageRequest Inspect(string id = RelationalSqlRf3Tokens.MessageId)
        => new(new(Partition, RelationalSqlRf3Tokens.Queue), id);

    internal ReadStreamRequest ReadStream(string id = RelationalSqlRf3Tokens.StreamId)
        => new(Stream with { StreamId = id });

    private static Task<ResourceDefinition> ConfigureAsync(KeyLoadClient administrator, PartitionRef partition,
        ResourceDefinition definition, CancellationToken cancellationToken)
        => SqlRf3Protocol.SdkAsync<ResourceDefinition>(administrator, SqlRf3Protocol.Call(partition,
            McpCallerTools.ResourcesConfigure, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition),
            Guid.NewGuid()), cancellationToken);
}
