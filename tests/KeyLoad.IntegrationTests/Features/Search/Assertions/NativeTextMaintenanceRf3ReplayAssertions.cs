using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3ReplayAssertions
{
    private const long NoPosition = 0;
    private const string Arguments = "arguments";
    private const int QueryLimit = 2;
    private const long ChangedRevision = 2;
    private const string Star = "*";
    private const string Sql = "CALL keyload_search_text_maintain(@arguments)";

    internal static async Task<TextIndexSourceCut> CurrentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest current, byte[] retained,
        NativeTextMaintenancePath path, CancellationToken token)
    {
        var original = JsonSerializer.Deserialize<TextIndexMaintenanceResult>(retained, JsonDefaults.Options)
            ?? throw new InvalidOperationException();
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var actual = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, current, path, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await NativeTextMaintenanceRf3Assertions.ResultAsync(actual, current);
        await StableAsync(actual, original);
        await SourceAsync(actual.Source!, original.Source!, before, after);
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
        await NativeTextSelectedRf3Assertions.HealthyAsync(sdk, mcp, scenario, current, path, changed: true, token);
        return actual.Source!;
    }

    internal static async Task OldAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest original,
        TextIndexMaintenanceRequest current, NativeTextMaintenancePath path, CancellationToken token)
    {
        var before = JsonDefaults.Serialize(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.OutboxStatusAsync(scenario.Partition, token)));
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
        await NativeTextSelectedRf3Assertions.HealthyAsync(sdk, mcp, scenario, current, path, changed: true, token);
        var originalPage = await QueryAsync(sdk, mcp, scenario, token);
        await RefuseAsync(sdk, mcp, original, path, token);
        var currentPage = await QueryAsync(sdk, mcp, scenario, token);
        await Assert.That(currentPage.AccessPath).IsEqualTo(originalPage.AccessPath);
        await Assert.That(currentPage.CutPosition).IsGreaterThanOrEqualTo(originalPage.CutPosition);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scenario.Partition, token));
        var official = (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await mcp.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(scenario.Partition), token))).Value;
        await Assert.That(JsonDefaults.Serialize(after).SequenceEqual(before)).IsTrue();
        await Assert.That(JsonDefaults.Serialize(official).SequenceEqual(before)).IsTrue();
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
        await NativeTextSelectedRf3Assertions.HealthyAsync(sdk, mcp, scenario, current, path, changed: true, token);
    }

    internal static async Task NoOpAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest current,
        TextIndexSourceCut expectedCut, string expectedIndexDigest, NativeTextMaintenancePath path, CancellationToken token)
    {
        var request = current with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Restore };
        var before = JsonDefaults.Serialize(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.OutboxStatusAsync(scenario.Partition, token)));
        var beforeFirst = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var first = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, request, path, token);
        var afterFirst = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await NativeTextMaintenanceRf3Assertions.ResultAsync(first, request);
        await Assert.That(first.Checkpoint).IsNull();
        await Assert.That(first.IndexSha256).IsEqualTo(expectedIndexDigest);
        await Assert.That(first.IndexedThroughSequence).IsEqualTo(expectedCut.ThroughSequence);
        await SourceAsync(first.Source!, expectedCut, beforeFirst, afterFirst);
        var beforeReplay = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, request, path, token);
        var afterReplay = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await StableAsync(replay, first);
        await SourceAsync(replay.Source!, first.Source!, beforeReplay, afterReplay);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scenario.Partition, token));
        await Assert.That(JsonDefaults.Serialize(after).SequenceEqual(before)).IsTrue();
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
        await NativeTextSelectedRf3Assertions.HealthyAsync(sdk, mcp, scenario, request, path, changed: true, token);
    }

    private static async Task<QueryPage> QueryAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, CancellationToken token)
    {
        var request = new AstQueryRequest(scenario.Partition,
            new SelectQuery(NativeTextMaintenanceRf3Scenario.Collection, null,
                [new(Star, Star)], null, [], QueryLimit), AllowFullScan: true);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAstAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryAst, request, token))).Value;
        QueryRow[] expected = [new(NativeTextMaintenanceRf3Scenario.Ukrainian, ChangedRevision,
            NativeTextMaintenanceRf3Scenario.ChangedJson, false, [])];
        await Assert.That(JsonDefaults.Serialize(direct.Rows).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(official.Rows).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(direct.Cursor).IsNull();
        await Assert.That(official.Cursor).IsNull();
        await Assert.That(direct.CutPosition).IsGreaterThan(NoPosition);
        await Assert.That(official.CutPosition).IsGreaterThanOrEqualTo(direct.CutPosition);
        await Assert.That(direct.AccessPath).IsNotNull();
        await Assert.That(official.AccessPath).IsEqualTo(direct.AccessPath);
        return direct;
    }

    private static async Task StableAsync(TextIndexMaintenanceResult actual, TextIndexMaintenanceResult original)
    {
        await Assert.That(JsonDefaults.Serialize(actual with { Source = null })
            .SequenceEqual(JsonDefaults.Serialize(original with { Source = null }))).IsTrue();
    }

    private static async Task SourceAsync(TextIndexSourceCut actual, TextIndexSourceCut original,
        NodeStatus before, NodeStatus after)
    {
        await Assert.That(actual.NodeId.ToString()).IsEqualTo(before.NodeId);
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(actual.NodeId).IsEqualTo(original.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(original.Incarnation);
        await Assert.That(actual.ThroughSequence).IsEqualTo(original.ThroughSequence);
        await Assert.That(actual.SchemaVersion).IsEqualTo(original.SchemaVersion);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(original.PolicyEpoch);
        await Assert.That(actual.ResourceSha256).IsEqualTo(original.ResourceSha256);
        await Assert.That(actual.Position).IsGreaterThan(NoPosition);
        await Assert.That(actual.Position).IsGreaterThanOrEqualTo(original.Position);
        await Assert.That(actual.AppliedPosition).IsGreaterThanOrEqualTo(before.Applied);
        await Assert.That(actual.AppliedPosition).IsLessThanOrEqualTo(after.Applied);
        await Assert.That(actual.AppliedPosition).IsGreaterThanOrEqualTo(original.AppliedPosition);
        await Assert.That(actual.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
        await Assert.That(actual.ReadGeneration).IsLessThanOrEqualTo(after.ReadGeneration);
        await Assert.That(actual.ReadGeneration).IsGreaterThanOrEqualTo(original.ReadGeneration);
    }

    private static async Task RefuseAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TextIndexMaintenanceRequest request, NativeTextMaintenancePath path, CancellationToken token)
    {
        if (path == NativeTextMaintenancePath.Sdk)
        {
            var actual = await sdk.MaintainTextIndexAsync(request, token);
            await ErrorAsync(actual.IsSuccess, actual.Problem?.ErrorCode);
            await Assert.That(actual.Value).IsNull();
            return;
        }
        var sql = new SqlOperationRequest(request.Consumer.Partition, Sql,
            new(StringComparer.Ordinal)
            { [Arguments] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(request), JsonDefaults.Options) });
        if (path == NativeTextMaintenancePath.SdkSql)
        {
            var actual = await sdk.ExecuteSqlAsync(sql, token);
            await ErrorAsync(actual.IsSuccess, actual.Problem?.ErrorCode);
            await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            return;
        }
        var reply = path switch
        {
            NativeTextMaintenancePath.Mcp => await mcp.CallAsync(TextIndexMaintenanceProtocol.ToolName, request, token),
            NativeTextMaintenancePath.McpSql => await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token),
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };
        _ = await McpCallerAssertions.ErrorAsync(reply, ErrorCode.HistoryUnavailable, dispatched: true);
    }

    private static async Task ErrorAsync(bool successful, string? errorCode)
    {
        await Assert.That(successful).IsFalse();
        await Assert.That(errorCode).IsEqualTo(ErrorCode.HistoryUnavailable.ToString());
    }
}
