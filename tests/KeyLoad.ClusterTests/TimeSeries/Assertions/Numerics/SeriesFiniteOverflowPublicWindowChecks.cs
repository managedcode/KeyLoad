using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SeriesFiniteOverflowPublicWindowChecks
{
    internal static async Task HealthyAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        var dense = SeriesFiniteOverflowPublicExpected.Windows(state, wide: false);
        await SeriesFiniteOverflowPublicObservation.ReadAsync(administrator, reader, state,
            McpCallerTools.SeriesWindows, dense, SeriesFiniteOverflowPublicExpected.DenseWindows(state.Purged),
            ct => reader.Sdk.AggregateSampleWindowsAsync(dense, ct), token);
        if (state.Purged)
        {
            var wide = SeriesFiniteOverflowPublicExpected.Windows(state, wide: true);
            await SeriesFiniteOverflowPublicObservation.ReadAsync(administrator, reader, state,
                McpCallerTools.SeriesWindows, wide, SeriesFiniteOverflowPublicExpected.RepairedWideWindow(),
                ct => reader.Sdk.AggregateSampleWindowsAsync(wide, ct), token);
        }
    }

    internal static async Task OverflowAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        await Assert.That(state.Purged).IsFalse();
        var request = SeriesFiniteOverflowPublicExpected.Windows(state, wide: true);
        var result = await reader.Sdk.AggregateSampleWindowsAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem).IsNotNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Validation,
            SeriesFiniteOverflowPublicExpected.OverflowDetail), result.Problem!);
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
        await HealthyAsync(administrator, reader, state, token);
        await SeriesFiniteOverflowPublicObservation.RefusalAsync(reader,
            McpCallerTools.SeriesWindows, request, token);
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
        await HealthyAsync(administrator, reader, state, token);
    }
}
