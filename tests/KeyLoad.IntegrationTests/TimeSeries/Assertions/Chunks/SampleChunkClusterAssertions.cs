using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkClusterAssertions
{
    internal static async Task CompleteAsync(SampleChunkClusterScenario scenario,
        SampleChunkWindowResult actual, CommitReceipt minimum)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        await Assert.That(minimum.Token.AtomicPartitionId).IsEqualTo(scenario.Execution.Partition.AtomicPartitionId);
        var expected = new SampleChunkWindowResult(scenario.WindowId, scenario.From, scenario.Until,
            SampleChunkClusterProtocol.MergedGeneration, SampleChunkClusterProtocol.MergedRevision,
            SampleChunkClusterProtocol.CorrectedSequence, null, [.. scenario.Expected], actual.CutPosition);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }

    internal static async Task AllReadRoutesAsync(SampleChunkClusterScenario scenario,
        KeyLoadClient sdk, McpOfficialClient mcp, CommitReceipt minimum, CancellationToken token)
    {
        var request = scenario.Request with
        {
            MinimumToken = minimum.Token
        };
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReadSampleChunkWindowAsync(request, token).ConfigureAwait(false));
        await CompleteAsync(scenario, sdkResult, minimum);
        var mcpResult = (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token).ConfigureAwait(false))).Value;
        await CompleteAsync(scenario, mcpResult, minimum);
        var call = SqlClusterProtocol.Call(scenario.Execution.Partition, SampleChunkProtocol.ReadTool, request);
        var sql = await SqlClusterProtocol.SdkAsync<SampleChunkWindowResult>(sdk, call, token).ConfigureAwait(false);
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
