using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SeriesFiniteOverflowPublicObservation
{
    internal static async Task ReadAsync<T>(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers caller, SeriesFiniteOverflowPublicState state, string tool,
        object request, T expected,
        Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> read,
        CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await read(token)));
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<T>(
                await caller.Mcp.CallAsync(tool, request, token))).Value);
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
    }

    internal static async Task RefusalAsync(RequestCqrsClusterCallers reader, string tool,
        object request, CancellationToken token)
    {
        var reply = await reader.Mcp.CallAsync(tool, request, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true)).Value;
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(SeriesFiniteOverflowPublicExpected.McpOverflowDetail);
    }
}
