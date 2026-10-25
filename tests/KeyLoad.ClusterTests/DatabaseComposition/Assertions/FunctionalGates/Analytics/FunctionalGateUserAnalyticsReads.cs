using System.Collections.Immutable;
using KeyLoad.ClusterTests.QueryExecution;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateUserAnalyticsReads
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateUserAnalyticsState state,
        ImmutableArray<AnalyticsEventRecord> rows, AnalyticsAggregateResult aggregate, CancellationToken token)
    {
        var request = new ReadAnalyticsEventsRequest(context.Partition, state.Store, state.From, state.Until, P.PageLimit, null);
        var expected = new AnalyticsEventsPage(rows, false, null);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Reader.Sdk.ReadAnalyticsEventsAsync(request, token)), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<AnalyticsEventsPage>(context.Reader,
            AnalyticsStoreProtocol.ReadTool, request, token), expected);
        var query = new AggregateAnalyticsRequest(context.Partition, state.Store, state.From, state.Until,
            [P.Kind], P.Selections, P.MaximumGroups);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Reader.Sdk.AggregateAnalyticsAsync(query, token)), aggregate);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<AnalyticsAggregateResult>(context.Reader,
            AnalyticsStoreProtocol.AggregateTool, query, token), aggregate);
        var sql = SqlClusterProtocol.Call(context.Partition, AnalyticsStoreProtocol.AggregateTool, query);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.SqlAsync<AnalyticsAggregateResult>(
            context, sql, official: false, write: false, token), aggregate);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.SqlAsync<AnalyticsAggregateResult>(
            context, sql, official: true, write: false, token), aggregate);
    }

    internal static async Task PageAsync(FunctionalGateContext context, FunctionalGateUserAnalyticsState state, CancellationToken token)
    {
        var request = new ReadAnalyticsEventsRequest(context.Partition, state.Store, state.From, state.Until, P.FirstRevision, null);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Reader.Sdk.ReadAnalyticsEventsAsync(request, token)), new AnalyticsEventsPage(
                [FunctionalGateUserAnalyticsState.Record(state.First, P.FirstRevision)], true, state.First.Id));
        request = request with
        {
            Limit = P.PageLimit,
            AfterId = state.First.Id
        };
        var expected = new AnalyticsEventsPage([FunctionalGateUserAnalyticsState.Record(state.Second, P.FirstRevision),
            FunctionalGateUserAnalyticsState.Record(state.Third, P.FirstRevision)], false, null);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<AnalyticsEventsPage>(context.Reader,
            AnalyticsStoreProtocol.ReadTool, request, token), expected);
        var sql = SqlClusterProtocol.Call(context.Partition, AnalyticsStoreProtocol.ReadTool, request);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.SqlAsync<AnalyticsEventsPage>(
            context, sql, official: false, write: false, token), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.SqlAsync<AnalyticsEventsPage>(
            context, sql, official: true, write: false, token), expected);
    }
}
