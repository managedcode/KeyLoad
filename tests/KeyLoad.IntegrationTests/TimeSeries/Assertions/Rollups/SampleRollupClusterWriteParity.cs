using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleRollupClusterWriteParity
{
    private const string CommitSql = "EXEC keyload_documents_commit @arguments";
    private const string Arguments = "arguments";
    private const int SampleLimit = 10;
    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, TimeSeriesClusterScenario scenario,
        CommandRequest original, CommitReceipt receipt, CancellationToken cancellation)
    {
        var rawRequest = new ReadSamplesRequest(scenario.Partition, TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Start, TimeSeriesClusterScenario.Start.AddMinutes(1), SampleLimit);
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSamplesAsync(rawRequest, cancellation));
        var sql = new SqlOperationRequest(scenario.Partition, CommitSql,
            new()
            {
                [Arguments] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(original), JsonDefaults.Options)
            });
        var sqlResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ExecuteSqlAsync(sql, cancellation));
        var sqlReceipt = sqlResult.Deserialize<CommitReceipt>(JsonDefaults.Options)!;
        var officialSql = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellation));
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, original, cancellation));
        var finalSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, cancellation));
        var expected = Convert.ToHexString(OrleansSerialization.Serialize(receipt));
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(sqlReceipt))).IsEqualTo(expected);
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(officialSql.Value))).IsEqualTo(expected);
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(official.Value))).IsEqualTo(expected);
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(finalSdk))).IsEqualTo(expected);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSamplesAsync(rawRequest, cancellation));
        var mcpRaw = await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await mcp.CallAsync(McpCallerTools.SeriesRead, rawRequest, cancellation));
        await Assert.That(JsonSerializer.Serialize(after, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(before, JsonDefaults.Options));
        await Assert.That(JsonSerializer.Serialize(mcpRaw.Value, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(before, JsonDefaults.Options));
        var literal = new SampleRecord[]
        {
            new(TimeSeriesClusterScenario.Series, new("a", TimeSeriesClusterScenario.Start, 2), 1, TimeSeriesClusterScenario.PublicTags),
            new(TimeSeriesClusterScenario.Series, new("b", TimeSeriesClusterScenario.Start.AddSeconds(30), 4), 2, TimeSeriesClusterScenario.PublicTags),
            new(TimeSeriesClusterScenario.Series, new("boundary", TimeSeriesClusterScenario.Start.AddMinutes(1), 8), 3, TimeSeriesClusterScenario.PublicTags)
        };
        await Assert.That(JsonSerializer.Serialize(after, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(literal, JsonDefaults.Options));
    }
}
