using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesExtremeAggregateWave
{
    private const int WindowMinutes = 2;
    private const int FirstIndex = 0;
    private const int SecondIndex = 1;
    private const int InitialSkip = 1;
    private const int InitialTake = 3;
    private const int FromOffsetHours = 4;
    private const int UntilOffsetHours = -2;
    private const int ShortBudget = 7;
    private const int FullBudget = 8;
    private const long FullCount = 8;
    private const double FullSum = 797;
    private const double FullMinimum = -8;
    private const double FullMaximum = 700;
    private const double FullAverage = 99.625;
    private const string BudgetDetail = "The time-series sample budget is exceeded.";
    private const string SafeBudgetDetail = "The operation exceeds the available resource capacity.";

    internal static async Task ReadAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader, RequestCqrsClusterCallers official,
        SeriesExtremeAggregateState state, SeriesExtremeAggregateRead read, CancellationToken token)
    {
        async Task Current()
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => SeriesExtremeAggregateCurrent.RequireAsync(administrator, reader, state, token), failures);
            await ServerFailureObserver.ObserveAsync(() => SeriesExtremeAggregateCurrent.RequireAsync(administrator, official, state, token), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        var start = TimeSeriesClusterScenario.Start;
        var end = start.AddMinutes(WindowMinutes);
        var expected = TimeSeriesClusterScenario.RawOracle(state.Initial.Skip(InitialSkip).Take(InitialTake).Append(state.Late[FirstIndex]));
        await read(reader.Sdk, official.Mcp, state.Scenario.Aggregate(start.ToOffset(TimeSpan.FromHours(FromOffsetHours)),
            end.ToOffset(TimeSpan.FromHours(UntilOffsetHours))), expected, Current, token);
        await read(reader.Sdk, official.Mcp, state.Scenario.Aggregate(start, start), TimeSeriesClusterScenario.RawOracle([]), Current, token);
        if (!state.HasExtrema)
        {
            return;
        }
        await read(reader.Sdk, official.Mcp, state.Scenario.Aggregate(DateTimeOffset.MaxValue, null),
            TimeSeriesClusterScenario.RawOracle([state.Extrema[FirstIndex]]), Current, token);
        await read(reader.Sdk, official.Mcp, state.Scenario.Aggregate(DateTimeOffset.MinValue, DateTimeOffset.MinValue.AddTicks(1)),
            TimeSeriesClusterScenario.RawOracle([state.Extrema[SecondIndex]]), Current, token);
        await RefuseAsync(reader, official, state, Current, token);
        await read(reader.Sdk, official.Mcp, state.Scenario.Aggregate(DateTimeOffset.MinValue, null, FullBudget),
            new(FullCount, FullSum, FullMinimum, FullMaximum, FullAverage), Current, token);
    }

    private static async Task RefuseAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers official, SeriesExtremeAggregateState state,
        Func<Task> current, CancellationToken token)
    {
        var request = state.Scenario.Aggregate(DateTimeOffset.MinValue, null, ShortBudget);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = await reader.Sdk.AggregateSamplesAsync(request, token);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(actual.Problem, Errors.Problem(ErrorCode.BudgetExceeded, BudgetDetail));
        }, failures);
        await ServerFailureObserver.ObserveAsync(current, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = await official.Mcp.CallAsync(McpCallerTools.SeriesAggregate, request, token);
            await McpCallerAssertions.ErrorAsync(result, ErrorCode.BudgetExceeded, dispatched: true);
            await SqlClusterProtocol.EqualAsync<object>(Errors.Problem(ErrorCode.BudgetExceeded, SafeBudgetDetail),
                result.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
        }, failures);
        await ServerFailureObserver.ObserveAsync(current, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        SeriesExtremeAggregateState state, CancellationToken token)
    {
        foreach (var (command, expected) in state.Receipts)
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token))), failures);
            await ServerFailureObserver.ObserveAsync(() => SeriesExtremeAggregateCurrent.RequireAsync(administrator, reader, state, token), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(expected,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, command, token))).Value), failures);
            await ServerFailureObserver.ObserveAsync(() => SeriesExtremeAggregateCurrent.RequireAsync(administrator, reader, state, token), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
