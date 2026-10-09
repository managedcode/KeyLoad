using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRf3Privacy
{
    private const long NextPolicy = 1;
    private const string PrivateTagValue = "private-marker";
    internal static async Task ExecuteAsync(ClusterFixture fixture, SampleChunkRf3Scenario scenario,
        KeyLoadClient administrator, CommitReceipt minimum, CancellationToken token)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Native.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead | Capability.Query, token).ConfigureAwait(false);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, token).ConfigureAwait(false);
        await ProjectedAsync(scenario, sdk, mcp, minimum, token);
        var privatePredicate = scenario.Request with
        { TagPointer = TimeSeriesRf3Scenario.SecretField, TagValue = PrivateTagValue };
        await DeniedAsync(scenario, sdk, mcp, privatePredicate, token);
        var revoked = identity.Principal with { Grants = [], PolicyEpoch = identity.Principal.PolicyEpoch + NextPolicy };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), revoked, token).ConfigureAwait(false));
        await DeniedAsync(scenario, sdk, mcp, scenario.Request, token);
        var restored = identity.Principal with { PolicyEpoch = revoked.PolicyEpoch + NextPolicy };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), restored, token).ConfigureAwait(false));
        await ProjectedAsync(scenario, sdk, mcp, minimum, token);
        await SampleChunkRf3Assertions.CompleteAsync(scenario, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.ReadSampleChunkWindowAsync(scenario.Request, token).ConfigureAwait(false)), minimum);
    }

    private static async Task ProjectedAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CommitReceipt minimum, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReadSampleChunkWindowAsync(scenario.Request, token).ConfigureAwait(false));
        await Assert.That(actual.CutPosition >= minimum.Token.Position).IsTrue();
        var expected = new SampleChunkWindowResult(scenario.WindowId, scenario.From, scenario.Until,
            SampleChunkRf3Protocol.MergedGeneration, SampleChunkRf3Protocol.MergedRevision,
            SampleChunkRf3Protocol.CorrectedSequence, null,
            [.. scenario.Expected.Select(row => row with { TagsJson = TimeSeriesRf3Scenario.ProjectedTags })], actual.CutPosition);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        var official = (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await mcp.CallAsync(SampleChunkProtocol.ReadTool, scenario.Request, token).ConfigureAwait(false))).Value;
        await Assert.That(official.CutPosition >= minimum.Token.Position).IsTrue();
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(official)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected with { CutPosition = official.CutPosition })));
        var call = SqlRf3Protocol.Call(scenario.Native.Partition, SampleChunkProtocol.ReadTool, scenario.Request);
        var sql = await SqlRf3Protocol.SdkAsync<SampleChunkWindowResult>(sdk, call, token).ConfigureAwait(false);
        var sqlOfficial = await SqlRf3Protocol.McpAsync<SampleChunkWindowResult>(mcp, call, token).ConfigureAwait(false);
        foreach (var row in new[] { sql, sqlOfficial })
        {
            await Assert.That(row.CutPosition >= minimum.Token.Position).IsTrue();
            await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(row)))
                .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected with { CutPosition = row.CutPosition })));
        }

    }

    private static async Task DeniedAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, ReadSampleChunkWindowRequest request, CancellationToken token)
    {
        var failure = await sdk.ReadSampleChunkWindowAsync(request, token).ConfigureAwait(false);
        await Assert.That(failure.IsSuccess).IsFalse();
        await Assert.That(failure.Value).IsNull();
        await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var official = await mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, TimeSeriesRf3Scenario.PrivateTags, PrivateTagValue);
        var call = SqlRf3Protocol.Call(scenario.Native.Partition, SampleChunkProtocol.ReadTool, request);
        var sql = await sdk.ExecuteSqlAsync(call, token).ConfigureAwait(false);
        await Assert.That(sql.IsSuccess).IsFalse();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(sql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var sqlOfficial = await mcp.CallAsync(SqlOperationProtocol.ToolName, call, token).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(sqlOfficial, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(sqlOfficial, TimeSeriesRf3Scenario.PrivateTags, PrivateTagValue);
    }
}
