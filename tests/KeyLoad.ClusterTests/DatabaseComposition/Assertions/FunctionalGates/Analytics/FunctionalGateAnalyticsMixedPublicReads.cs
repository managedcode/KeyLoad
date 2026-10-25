using KeyLoad.ClusterTests.ClientApi;
using A = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateAnalyticsMixedPublicProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsMixedPublicReads
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateAnalyticsMixedPublicState state,
        bool present, long minimumCut, CancellationToken token)
    {
        await DocumentAsync(context, state, present, token);
        await GraphAsync(context, state, present, token);
        await AnalyticsAsync(context, state, present, token);
        await FunctionalGateAnalyticsMixedPublicEvents.VerifyAsync(context, state, present, minimumCut, token);
    }

    private static async Task DocumentAsync(FunctionalGateContext context, FunctionalGateAnalyticsMixedPublicState state,
        bool present, CancellationToken token)
    {
        var expected = new DocumentReadResult(present
            ? new(state.Document, P.Created, P.DocumentJson, false, []) : null);
        foreach (var actual in await FunctionalGateAnalyticsMixedPublicCalls.ReadAsync(context, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(state.Document), context.Reader.Sdk.GetAsync(state.Document, token), token))
        {
            await FunctionalGateUserAnalyticsAssertions.EqualAsync(actual, expected);
        }
    }

    private static async Task GraphAsync(FunctionalGateContext context, FunctionalGateAnalyticsMixedPublicState state,
        bool present, CancellationToken token)
    {
        var request = new TraverseRequest(context.Partition, P.Graph, context.Bind(context.Anchor), P.Created,
            P.MaximumVertices, P.MaximumEdges, [state.Label]);
        var vertices = new[] { context.Bind(context.Anchor), state.Document }.OrderBy(value => value.Collection, StringComparer.Ordinal)
            .ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
        var expected = present ? new global::KeyLoad.GraphTraversal([.. vertices],
            [new(state.EdgeId, context.Bind(context.Anchor), state.Document, state.Label, P.Attributes, P.Created)])
            : new global::KeyLoad.GraphTraversal([context.Bind(context.Anchor)], []);
        foreach (var actual in await FunctionalGateAnalyticsMixedPublicCalls.ReadAsync(context, McpCallerTools.GraphTraverse,
            request, context.Reader.Sdk.TraverseAsync(request, token), token))
        {
            await FunctionalGateUserAnalyticsAssertions.EqualAsync(actual, expected);
        }
    }

    private static async Task AnalyticsAsync(FunctionalGateContext context, FunctionalGateAnalyticsMixedPublicState state,
        bool present, CancellationToken token)
    {
        var request = new ReadAnalyticsEventsRequest(context.Partition, state.Store, state.Event.Timestamp,
            state.Event.Timestamp.AddMinutes(P.Created), P.PageLimit, null);
        var expected = new AnalyticsEventsPage(present
            ? [FunctionalGateUserAnalyticsState.Record(state.Event, P.Created)] : [], false, null);
        foreach (var actual in await FunctionalGateAnalyticsMixedPublicCalls.ReadAsync(context, AnalyticsStoreProtocol.ReadTool,
            request, context.Reader.Sdk.ReadAnalyticsEventsAsync(request, token), token))
        {
            await FunctionalGateUserAnalyticsAssertions.EqualAsync(actual, expected);
        }

        var aggregate = new AggregateAnalyticsRequest(context.Partition, state.Store, request.FromInclusive, request.ToExclusive,
            [A.Kind], A.Selections, P.MaximumGroups);
        foreach (var actual in await FunctionalGateAnalyticsMixedPublicCalls.ReadAsync(context, AnalyticsStoreProtocol.AggregateTool,
            aggregate, context.Reader.Sdk.AggregateAnalyticsAsync(aggregate, token), token))
        {
            await FunctionalGateUserAnalyticsAssertions.EqualAsync(actual, FunctionalGateAnalyticsMixedPublicState.Aggregate(present));
        }
    }
}
