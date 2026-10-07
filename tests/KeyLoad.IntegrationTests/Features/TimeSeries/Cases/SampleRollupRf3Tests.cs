using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>Explicit rollups traverse actual RF3 request grains through SDK, official MCP and Q1 CALL.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleRollupRf3Tests(ClusterFixture fixture)
{
    private const string ReadSql = "CALL keyload_series_read_rollup(@arguments)";
    private const string Arguments = "arguments";
    [Test]
    public async Task AcSeries024NativeSdkOfficialMcpSqlLateCorrectionAndOriginalReceiptParity()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, deadline.Token);
        var start = TimeSeriesRf3Scenario.Start;
        var end = start.AddMinutes(1);
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series,
            [new("a", start, 2), new("b", start.AddSeconds(30), 4), new("boundary", end, 8)],
            TimeSeriesRf3Scenario.PublicTags, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesManage | Capability.SeriesRead, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, TimeSeriesRf3Scenario.Node3);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, TimeSeriesRf3Scenario.Node2,
            identity.Secret, deadline.Token);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new RefreshSampleRollup(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, start, end, 0)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token));
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, deadline.Token));
        await Assert.That(JsonSerializer.Serialize(replay.Value, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(receipt, JsonDefaults.Options));
        await SampleRollupRf3WriteParity.VerifyAsync(sdk, mcp, scenario, command, receipt, deadline.Token);
        var request = new ReadSampleRollupRequest(scenario.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, start, end);
        await LiteralAsync(sdk, mcp, request, new(1, new(start, end, 3, null, new(2, 6, 2, 4, 3))), deadline.Token);
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series,
            [new("late", start.AddSeconds(15), 6)], TimeSeriesRf3Scenario.PublicTags, deadline.Token);
        await Assert.That((await sdk.ReadSampleRollupAsync(request, deadline.Token)).Problem?.ErrorCode)
            .IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SampleRollupProtocol.ReadTool, request, deadline.Token),
            ErrorCode.HistoryUnavailable, dispatched: true);
        var correction = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new RefreshSampleRollup(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, start, end, 1)]);
        var corrected = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, correction, deadline.Token));
        var correctedReplay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(correction, deadline.Token));
        await Assert.That(JsonSerializer.Serialize(corrected.Value, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(correctedReplay, JsonDefaults.Options));
        await LiteralAsync(sdk, mcp, request, new(2, new(start, end, 4, null, new(3, 12, 2, 6, 4))), deadline.Token);
    }
    private static async Task LiteralAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadSampleRollupRequest request, SampleRollupResult expected, CancellationToken cancellation)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSampleRollupAsync(request, cancellation));
        var official = await McpCallerAssertions.SuccessAsync<SampleRollupResult>(
            await mcp.CallAsync(SampleRollupProtocol.ReadTool, request, cancellation));
        var sql = new SqlOperationRequest(request.Partition, ReadSql,
            new() { [Arguments] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(request), JsonDefaults.Options) });
        var sdkSql = await McpCallerAssertions.SdkSuccessAsync(await sdk.ExecuteSqlAsync(sql, cancellation));
        var mcpSql = await McpCallerAssertions.SuccessAsync<SampleRollupResult>(
            await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellation));
        await Assert.That(direct).IsEqualTo(expected);
        await Assert.That(official.Value).IsEqualTo(expected);
        await Assert.That(sdkSql.Deserialize<SampleRollupResult>(JsonDefaults.Options)).IsEqualTo(expected);
        await Assert.That(mcpSql.Value).IsEqualTo(expected);
    }
}
