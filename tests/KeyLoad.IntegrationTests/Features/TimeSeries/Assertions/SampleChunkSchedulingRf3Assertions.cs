using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSchedulingRf3Assertions
{
    internal static async Task PendingAsync(SampleChunkJobRevocationScenario scenario,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var minimum = scenario.Correction ?? throw new InvalidOperationException(SampleChunkJobRevocationProtocol.Missing);
        var request = scenario.Window.Request;
        await CompleteAsync(scenario, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReadSampleChunkWindowAsync(request, token).ConfigureAwait(false)), minimum);
        await CompleteAsync(scenario, (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token).ConfigureAwait(false))).Value, minimum);
        var sql = SqlRf3Protocol.Call(scenario.Window.Native.Partition, SampleChunkProtocol.ReadTool, request);
        await CompleteAsync(scenario, await SqlRf3Protocol.SdkAsync<SampleChunkWindowResult>(sdk, sql, token)
            .ConfigureAwait(false), minimum);
        await CompleteAsync(scenario, (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token).ConfigureAwait(false))).Value, minimum);
    }

    private static async Task CompleteAsync(SampleChunkJobRevocationScenario scenario,
        SampleChunkWindowResult actual, CommitReceipt minimum)
    {
        await Assert.That(actual.CutPosition >= minimum.Token.Position).IsTrue();
        var expected = new SampleChunkWindowResult(scenario.Window.WindowId, scenario.Window.From,
            scenario.Window.Until, SampleChunkRf3Protocol.SealedGeneration, SampleChunkRf3Protocol.CorrectedRevision,
            SampleChunkRf3Protocol.CorrectedSequence, null, [.. scenario.Window.Expected], actual.CutPosition);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }
}
