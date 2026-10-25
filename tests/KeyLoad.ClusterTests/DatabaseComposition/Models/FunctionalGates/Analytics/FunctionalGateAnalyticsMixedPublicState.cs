using System.Collections.Immutable;
using System.Text.Json;
using A = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateAnalyticsMixedPublicProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsMixedPublicState(string Store, EntityRef Document, string EdgeId,
    string Label, StreamRef Stream, AnalyticsEventData Event, EventData Appended, long OriginalGeneration)
{
    internal static FunctionalGateAnalyticsMixedPublicState Create(FunctionalGateContext context, int index)
    {
        var id = P.Prefix + Guid.NewGuid().ToString(FunctionalGateProtocol.GuidFormat);
        var store = FunctionalGateUserAnalyticsState.StoreName(context.WriterPrincipalId, index);
        var from = new DateTimeOffset(P.Year, P.Month, P.Day, P.Empty, P.Empty, P.Empty, TimeSpan.Zero).AddMinutes(index);
        var document = context.Bind(new(context.Partition, context.MutableDocumentCollection, id + P.DocumentSuffix));
        var value = new AnalyticsEventData(id + P.EventSuffix, from,
            ImmutableDictionary<string, string>.Empty.Add(A.Cohort, id).Add(A.Kind, A.Alpha),
            ImmutableDictionary<string, double>.Empty.Add(A.Value, P.Value).Add(A.Weight, A.WeightValue), null, [context.Bind(context.Anchor)]);
        var payload = JsonSerializer.Serialize(new
        {
            document,
            analyticsStore = store,
            analyticsId = value.Id
        }, JsonDefaults.Options);
        return new(store, document, id + P.EdgeSuffix, id,
            new(context.Partition, FunctionalGateEventsProtocol.Resource(P.Empty), id + P.StreamSuffix), value,
            new(id + P.EventSuffix, P.EventType, payload, OccurredAt: from, CorrelationId: id, CausationId: document.Id),
            context.AnalyticsGenerations[store]);
    }

    internal CommandRequest Command(FunctionalGateContext context, Guid id, long analyticsExpectedRevision)
        => new(id, context.Partition,
        [
            new PutDocument(Document.Collection, Document.Id, P.DocumentJson, P.Empty),
            new UpsertAnalyticsEvents(Store, [Event], ImmutableDictionary<string, long>.Empty.Add(Event.Id, analyticsExpectedRevision)),
            new UpsertEdge(P.Graph, EdgeId, context.Bind(context.Anchor), Document, Label, P.Attributes, P.Empty),
            new AppendEvents(Stream.StreamSet, Stream.StreamId, [Appended], ExpectedStreamRevision.NoStream)
        ]);

    internal ImmutableArray<MutationReceipt> Effects =>
    [
        new(P.PutKind, Document.Collection, Document.Id, P.Created),
        new(AnalyticsStoreProtocol.UpsertKind, Store, Event.Id, checked(OriginalGeneration + P.Created)),
        new(P.EdgeKind, P.Graph, EdgeId, P.Created),
        new(P.AppendKind, Stream.StreamSet, Stream.StreamId, P.Created)
    ];

    internal static AnalyticsAggregateResult Aggregate(bool present)
        => present ? new([new(ImmutableDictionary<string, string>.Empty.Add(A.Kind, A.Alpha),
            ImmutableDictionary<string, double>.Empty.Add(A.Count, P.Created).Add(A.Sum, P.Value)
                .Add(A.Average, P.Value).Add(A.Minimum, P.Value).Add(A.Maximum, P.Value))], P.Created) : new([], P.Empty);
}
