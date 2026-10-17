using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.Authorization;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsCompositionData
{
    internal const string Graph = "analytics-composition-graph";
    internal const string Events = "analytics-composition-events";
    internal const string Writer = "analytics-composition-writer";
    internal const string First = "mixed-first";
    internal const string Second = "mixed-second";
    internal const string Rejected = "mixed-rejected";
    internal const string ColdFirst = "mixed-cold-first";
    internal const string ColdSecond = "mixed-cold-second";
    internal const string Denied = "mixed-denied";
    internal const string Label = "derived-from";
    internal const string Created = "Created";
    internal const string Json = "{\"value\":7}";
    internal const string EmptyJson = "{}";
    internal const string Put = "putDocument";
    internal const string Upsert = "upsertAnalyticsEvents";
    internal const string Edge = "upsertEdge";
    internal const string Append = "appendEvents";
    internal const string CasDetail = "The expected analytics event revision does not match.";
    internal const string DeniedDetail = "The principal cannot perform this operation in this scope.";

    internal static PrincipalRecord Principal(SafeProjectorOperationDatabase owner, long epoch, bool write)
        => new(Writer, owner.Source.Partition.TenantId,
        [new(owner.Source.Partition.DatabaseId, D.Store, Capability.AnalyticsRead | (write ? Capability.AnalyticsWrite : Capability.None)),
         new(owner.Source.Partition.DatabaseId, D.AnchorCollection, Capability.DocumentsRead | Capability.DocumentsWrite),
         new(owner.Source.Partition.DatabaseId, Graph, Capability.GraphRead | Capability.GraphWrite),
         new(owner.Source.Partition.DatabaseId, Events, Capability.EventsRead | Capability.EventsAppend)], [])
        {
            PolicyEpoch = epoch
        };

    internal static ImmutableArray<Mutation> Mutations(SafeProjectorOperationDatabase owner, string id, bool invalid = false)
        => [new PutDocument(D.AnchorCollection, id, Json, 0),
            new UpsertAnalyticsEvents(D.Store, [D.Event(owner.Source, id, D.Alpha, 7)],
                AnalyticsStoreNativeTestOperations.Revisions((id, invalid ? 1 : 0))),
            new UpsertEdge(Graph, id, new(owner.Source.Partition, D.AnchorCollection, id),
                new(owner.Source.Partition, D.AnchorCollection, D.AnchorId), Label, EmptyJson, 0),
            new AppendEvents(Events, id, [new(id, Created, EmptyJson)], ExpectedStreamRevision.NoStream)];
}
