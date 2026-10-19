using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

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
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(SeriesFiniteOverflowPublicExpected.McpOverflowDetail);
    }
}
