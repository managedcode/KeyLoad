using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnLineageTestData
{
    private const string StreamSet = "ann-lineage-events";
    private const string Stream = "ann-lineage-stream";
    private const string Source = "ann-lineage-source";
    private const string Event = "ann-lineage-event";
    private const string Input = "/input";
    private const string Classification = "embedding";
    private const string HiddenClassification = "ann-hidden";
    private const string Reducer = "ann-lineage-reducer";
    private const string Version = "v1";
    private const long Revision = 1;

    internal static void Seed(TestDatabase database)
    {
        Policy(database, hidden: false);
        database.Configure(StreamSet, ResourceKind.StreamSet);
        database.Commit(new PutDocument(AnnSeedTestSupport.Collection, Source, "{\"input\":\"original\"}"),
            new AppendEvents(StreamSet, Stream, [new(Event, "Created", "{}")], ExpectedStreamRevision.NoStream));
        database.Commit(new ApplyVectorProjection(new(database.Partition, StreamSet, Stream, Revision), Revision, Event,
            new(database.Partition, AnnSeedTestSupport.Collection, Source), Revision, Input, Reducer, Version, Revision,
            new(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field, [3, 2, 1], AnnSeedTestSupport.Space(), Revision)));
    }
    internal static void ChangeSource(TestDatabase database)
        => database.Commit(new PatchDocument(AnnSeedTestSupport.Collection, Source,
            [new(Input, PatchKind.Set, "\"changed\"")], Revision));

    internal static void Reproject(TestDatabase database)
    {
        const long Updated = 2;
        const string NextEvent = "ann-lineage-event-2";
        database.Commit(new AppendEvents(StreamSet, Stream, [new(NextEvent, "Updated", "{}")], ExpectedStreamRevision.Exact(Revision)),
            new ApplyVectorProjection(new(database.Partition, StreamSet, Stream, Revision), Updated, NextEvent,
                new(database.Partition, AnnSeedTestSupport.Collection, Source), Updated, Input, Reducer, Version, Updated,
                new(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field, [4, 3, 2], AnnSeedTestSupport.Space(), Revision)));
    }

    internal static void Policy(TestDatabase database, bool hidden)
    {
        var current = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, AnnSeedTestSupport.Collection)))
            ?? throw new InvalidOperationException("The native lineage source collection is absent.");
        var definition = current with
        {
            SchemaVersion = checked(current.SchemaVersion + Revision),
            FieldPolicies = [new(AnnSeedTestSupport.Field, Classification, AnnSeedTestSupport.FieldRead,
                AnnSeedTestSupport.FieldUse, AnnSeedTestSupport.FieldWrite),
                new(Input, hidden ? HiddenClassification : Classification)]
        };
        _ = database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition)
            { ExpectedSchemaVersion = current.SchemaVersion }).Get<ResourceDefinition>();
    }
}
