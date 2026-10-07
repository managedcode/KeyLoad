using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleRollupRf3WriteParity
{
    private const string CommitSql = "CALL keyload_documents_commit(@arguments)";
    private const string Arguments = "arguments";
    private const int SampleLimit = 10;
    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, TimeSeriesRf3Scenario scenario,
        CommandRequest original, CommitReceipt receipt, CancellationToken cancellation)
    {
        var rawRequest = new ReadSamplesRequest(scenario.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start.AddMinutes(1), SampleLimit);
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSamplesAsync(rawRequest, cancellation));
        var sql = new SqlOperationRequest(scenario.Partition, CommitSql,
            new() { [Arguments] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(original), JsonDefaults.Options) });
        var sqlResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ExecuteSqlAsync(sql, cancellation));
        var sqlReceipt = sqlResult.Deserialize<CommitReceipt>(JsonDefaults.Options)!;
        var officialSql = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellation));
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, original, cancellation));
        var finalSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, cancellation));
        var expected = Convert.ToHexString(NativeSerialization.Serialize(receipt));
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(sqlReceipt))).IsEqualTo(expected);
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(officialSql.Value))).IsEqualTo(expected);
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(official.Value))).IsEqualTo(expected);
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(finalSdk))).IsEqualTo(expected);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSamplesAsync(rawRequest, cancellation));
        var mcpRaw = await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await mcp.CallAsync(McpCallerTools.SeriesRead, rawRequest, cancellation));
        await Assert.That(JsonSerializer.Serialize(after, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(before, JsonDefaults.Options));
        await Assert.That(JsonSerializer.Serialize(mcpRaw.Value, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(before, JsonDefaults.Options));
        var literal = new SampleRecord[]
        {
            new(TimeSeriesRf3Scenario.Series, new("a", TimeSeriesRf3Scenario.Start, 2), 1, TimeSeriesRf3Scenario.PublicTags),
            new(TimeSeriesRf3Scenario.Series, new("b", TimeSeriesRf3Scenario.Start.AddSeconds(30), 4), 2, TimeSeriesRf3Scenario.PublicTags),
            new(TimeSeriesRf3Scenario.Series, new("boundary", TimeSeriesRf3Scenario.Start.AddMinutes(1), 8), 3, TimeSeriesRf3Scenario.PublicTags)
        };
        await Assert.That(JsonSerializer.Serialize(after, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(literal, JsonDefaults.Options));
    }
}
