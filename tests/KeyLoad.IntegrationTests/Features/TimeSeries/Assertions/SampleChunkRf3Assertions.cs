using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRf3Assertions
{
    internal static async Task CompleteAsync(SampleChunkRf3Scenario scenario,
        SampleChunkWindowResult actual, CommitReceipt minimum)
    {
        await Assert.That(actual.CutPosition >= minimum.Token.Position).IsTrue();
        var expected = new SampleChunkWindowResult(scenario.WindowId, scenario.From, scenario.Until,
            SampleChunkRf3Protocol.MergedGeneration, SampleChunkRf3Protocol.MergedRevision,
            SampleChunkRf3Protocol.CorrectedSequence, null, [.. scenario.Expected], actual.CutPosition);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }

    internal static async Task AllReadRoutesAsync(SampleChunkRf3Scenario scenario,
        KeyLoadClient sdk, McpOfficialClient mcp, CommitReceipt minimum, CancellationToken token)
    {
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReadSampleChunkWindowAsync(scenario.Request, token).ConfigureAwait(false));
        await CompleteAsync(scenario, sdkResult, minimum);
        var mcpResult = (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SampleChunkProtocol.ReadTool, scenario.Request, token).ConfigureAwait(false))).Value;
        await CompleteAsync(scenario, mcpResult, minimum);
        var call = SqlRf3Protocol.Call(scenario.Native.Partition, SampleChunkProtocol.ReadTool, scenario.Request);
        var sql = await SqlRf3Protocol.SdkAsync<SampleChunkWindowResult>(sdk, call, token).ConfigureAwait(false);
        await CompleteAsync(scenario, sql, minimum);
        var sqlMcp = (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SqlOperationProtocol.ToolName, call, token).ConfigureAwait(false))).Value;
        await CompleteAsync(scenario, sqlMcp, minimum);
    }

    internal static async Task OriginalReceiptAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest original, CommitReceipt receipt, CancellationToken token)
    {
        var sdkReplay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, token).ConfigureAwait(false));
        var mcpReplay = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, original, token).ConfigureAwait(false))).Value;
        var expected = Convert.ToHexString(JsonDefaults.Serialize(receipt));
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(sdkReplay))).IsEqualTo(expected);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(mcpReplay))).IsEqualTo(expected);
    }
}
