using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsColdAssertions
{
    internal static Task VerifyAsync(FunctionalGateContext context, FunctionalGateAnalyticsColdStore proof,
        CancellationToken token)
        => StateAsync(context, proof.State, FunctionalGateAnalyticsColdExpected.UpdatedRows(proof.State),
            FunctionalGateUserAnalyticsExpected.Updated, token);

    internal static async Task StateAsync(FunctionalGateContext context, FunctionalGateUserAnalyticsState state,
        ImmutableArray<AnalyticsEventRecord> rows, AnalyticsAggregateResult aggregate, CancellationToken token)
    {
        await FunctionalGateUserAnalyticsReads.VerifyAsync(context, state, rows, aggregate, token);
        var request = new ReadAnalyticsEventsRequest(context.Partition, state.Store, state.From, state.Until, P.PageLimit, null);
        var expected = new AnalyticsEventsPage(rows, false, null);
        var sql = SqlRf3Protocol.Call(context.Partition, AnalyticsStoreProtocol.ReadTool, request);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.SqlAsync<AnalyticsEventsPage>(
            context, sql, official: false, write: false, token), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.SqlAsync<AnalyticsEventsPage>(
            context, sql, official: true, write: false, token), expected);
    }
}
