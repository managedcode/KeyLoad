namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsPolicyWitness
{
    private const long Revision = 1;
    private const int Limit = 3;

    internal static async Task FullAsync(FunctionalGateContext context, FunctionalGateAnalyticsPolicyState state,
        CancellationToken token)
    {
        var request = Request(context, state);
        var expected = new AnalyticsEventsPage([FunctionalGateUserAnalyticsState.Record(state.Owned, Revision),
            FunctionalGateUserAnalyticsState.Record(state.Foreign, Revision)], false, null);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Administrator.Sdk.ReadAnalyticsEventsAsync(request, token)), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<AnalyticsEventsPage>(context.Administrator,
            AnalyticsStoreProtocol.ReadTool, request, token), expected);
        var query = new AggregateAnalyticsRequest(context.Partition, state.Query.Store, state.Query.From,
            state.Query.Until, [FunctionalGateUserAnalyticsProtocol.Kind], FunctionalGateUserAnalyticsProtocol.Selections,
            FunctionalGateUserAnalyticsProtocol.MaximumGroups);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Administrator.Sdk.AggregateAnalyticsAsync(query, token)), FunctionalGateAnalyticsPolicyState.FullExpected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<AnalyticsAggregateResult>(context.Administrator,
            AnalyticsStoreProtocol.AggregateTool, query, token), FunctionalGateAnalyticsPolicyState.FullExpected);
    }

    internal static async Task OwnedAsync(FunctionalGateContext context, FunctionalGateAnalyticsPolicyState state,
        CancellationToken token)
    {
        var expected = new AnalyticsEventsPage([FunctionalGateUserAnalyticsState.Record(state.Owned, Revision)], false, null);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Reader.Sdk.ReadAnalyticsEventsAsync(Request(context, state), token)), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<AnalyticsEventsPage>(context.Reader,
            AnalyticsStoreProtocol.ReadTool, Request(context, state), token), expected);
    }

    private static ReadAnalyticsEventsRequest Request(FunctionalGateContext context, FunctionalGateAnalyticsPolicyState state)
        => new(context.Partition, state.Query.Store, state.Query.From, state.Query.Until, Limit, null);
}
