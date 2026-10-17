using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesFiniteOverflowPublicObservation
{
    internal static async Task ReadAsync<T>(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers caller, SeriesFiniteOverflowPublicState state, string tool,
        object request, T expected,
        Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> read,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await read(token)));
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<T>(
                await caller.Mcp.CallAsync(tool, request, token))).Value);
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
    }

    internal static async Task RefusalAsync(RequestCqrsRf3Callers reader, string tool,
        object request, CancellationToken token)
    {
        var reply = await reader.Mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(SeriesFiniteOverflowPublicExpected.McpOverflowDetail);
    }
}
