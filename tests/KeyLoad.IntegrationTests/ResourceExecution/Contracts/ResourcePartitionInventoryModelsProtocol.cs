using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsProtocol
{
    internal const int Index = 0;
    internal const int PerFamily = 1;
    internal const int Version = 1;
    internal const int PublicProtocols = 2;
    internal const long InitialRevision = 1L;
    internal const long MissingRevision = 0L;
    internal const string Domain = "d2-inventory-models-domain";
    internal const string PartitionKey = "models-original";
    internal const string AnchorId = "anchor";
    internal const string AnchorJson = "{\"purpose\":\"cold-anchor\"}";
    internal const string PutKind = "putDocument";
    internal const string TableCreateSql = "CREATE TABLE [cold_table_0] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY,[value] BIGINT NOT NULL,[reference] NVARCHAR(MAX) NOT NULL)";

    internal static PartitionRef Partition => new(ResourcePartitionInventoryRf3Protocol.Tenant,
        ResourcePartitionInventoryRf3Protocol.Database, Domain, PartitionKey);
    internal static string Resource(string family) => FunctionalGateColdProtocol.Resource(family, Index);
    internal static EntityRef Anchor => new(Partition, Resource("docs"), AnchorId);
    internal static EntityRef Document => new(Partition, Resource("docs"), FunctionalGateColdProtocol.Id);
    internal static BlobRef Blob => new(Partition, Resource("blobs"), FunctionalGateColdProtocol.Id);

    internal static ResourceDefinition TableDefinition => new(Resource("table"), ResourceKind.Collection, Domain)
    {
        Authority = DocumentAuthority.Document,
        RelationalSchema = new(FunctionalGateColdProtocol.IdColumn,
        [
            new(FunctionalGateColdProtocol.IdColumn, RelationalColumnType.Text),
            new("value", RelationalColumnType.WholeNumber),
            new("reference", RelationalColumnType.Text)
        ])
    };

    internal static ResourceDefinition Definition(string family, ResourceKind kind)
        => new(Resource(family), kind, Domain);

    internal static ImmutableArray<ResourcePartitionInventoryModelsStep> Steps =>
    [
        new(Definition("docs", ResourceKind.Collection),
            [new PutDocument(Resource("docs"), AnchorId, AnchorJson, MissingRevision),
             new PutDocument(Resource("docs"), FunctionalGateColdProtocol.Id,
                FunctionalGateColdProtocol.DocumentJson(Anchor, Blob), MissingRevision)],
            [new(PutKind, Resource("docs"), AnchorId, InitialRevision),
             new(PutKind, Resource("docs"), FunctionalGateColdProtocol.Id, InitialRevision)]),
        new(Definition("vectors", ResourceKind.Collection) with
            { VectorProfiles = [new(FunctionalGateSearch.VectorField, FunctionalGateSearch.Space)] },
            [new PutDocument(Resource("vectors"), FunctionalGateColdProtocol.Id, FunctionalGateColdProtocol.Vector, MissingRevision),
             new PutVector(Resource("vectors"), FunctionalGateColdProtocol.Id, FunctionalGateSearch.VectorField,
                [1, 0], FunctionalGateSearch.Space, InitialRevision)],
            [new(PutKind, Resource("vectors"), FunctionalGateColdProtocol.Id, InitialRevision),
             new("putVector", Resource("vectors"), FunctionalGateColdProtocol.Id, InitialRevision)]),
        new(Definition("text", ResourceKind.Collection),
            [new PutDocument(Resource("text"), FunctionalGateColdProtocol.Id, FunctionalGateColdProtocol.Text, MissingRevision)],
            [new(PutKind, Resource("text"), FunctionalGateColdProtocol.Id, InitialRevision)]),
        new(Definition("events", ResourceKind.StreamSet),
            [new AppendEvents(Resource("events"), FunctionalGateColdProtocol.Id,
                [FunctionalGateColdProtocol.Data(Document)], ExpectedStreamRevision.NoStream)],
            [new("appendEvents", Resource("events"), FunctionalGateColdProtocol.Id, InitialRevision)]),
        new(Definition("queue", ResourceKind.WorkQueue),
            [new EnqueueMessage(Resource("queue"), FunctionalGateColdProtocol.Id, FunctionalGateColdProtocol.Data(Document).PayloadJson)],
            [new("enqueue", Resource("queue"), FunctionalGateColdProtocol.Id, InitialRevision)]),
        new(Definition("topic", ResourceKind.Topic),
            [new PublishTopic(Resource("topic"), [FunctionalGateColdProtocol.Data(Document)])],
            [new("publishTopic", Resource("topic"), FunctionalGateColdProtocol.Id, InitialRevision)]),
        new(Definition("graph", ResourceKind.Graph),
            [new UpsertEdge(Resource("graph"), FunctionalGateColdProtocol.Edge, Document, Anchor, "retained", "{}", MissingRevision)],
            [new("upsertEdge", Resource("graph"), FunctionalGateColdProtocol.Edge, InitialRevision)]),
        new(Definition("series", ResourceKind.TimeSeries),
            [new AppendSamples(Resource("series"), FunctionalGateColdProtocol.Series,
                [.. FunctionalGateColdProtocol.Samples], FunctionalGateColdProtocol.Tags(Anchor))],
            [new("appendSamples", Resource("series"), FunctionalGateColdProtocol.Series, 2)])
    ];
}
