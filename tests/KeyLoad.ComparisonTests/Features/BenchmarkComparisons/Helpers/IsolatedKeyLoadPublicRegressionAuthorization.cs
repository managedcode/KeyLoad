using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004: persisted read-only authority permits reads and denies SDK, MCP and SQL writes without effects or secrets.</summary>
internal static class IsolatedKeyLoadPublicRegressionAuthorization
{
    internal static async Task VerifyAsync(DistributedApplication app, KeyLoadClient administrator,
        IsolatedKeyLoadPublicRegressionScenario scenario, int nodeCount, CancellationToken token)
    {
        var identity = await IsolatedKeyLoadPublicRegressionIdentity.CreateAsync(administrator, scenario.Partition,
            scenario.Sentinel.Collection, Capability.DocumentsRead, token);
        using var http = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, 1);
        var reader = new KeyLoadClient(http, identity.Secret, ComparisonClientOptions.Execution());
        await using var mcp = await IsolatedKeyLoadPublicRegressionMcp.ConnectAsync(app, nodeCount, identity.Secret, token);
        var request = new GetDocumentRequest(scenario.Sentinel);
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await reader.GetAsync(scenario.Sentinel, token)))!,
            scenario.Sentinel, scenario.SentinelJson, 1);
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            await mcp.SuccessAsync<DocumentResult>(IsolatedKeyLoadPublicRegressionProtocol.Get, request, token),
            scenario.Sentinel, scenario.SentinelJson, 1);
        var denied = scenario.Command(new PutDocument(scenario.Sentinel.Collection, scenario.Sentinel.Id,
            scenario.UpdatedJson, 1, ExplicitReplacement: true));
        await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(await reader.CommitAsync(denied, token),
            ErrorCode.PermissionDenied, identity.Secret, scenario.PrivateValue);
        await mcp.ErrorAsync(IsolatedKeyLoadPublicRegressionProtocol.Commit, denied, ErrorCode.PermissionDenied, token,
            identity.Secret, scenario.PrivateValue);
        var sql = IsolatedKeyLoadPublicRegressionProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicRegressionProtocol.Commit, denied);
        await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(await reader.ExecuteSqlAsync(sql, token),
            ErrorCode.PermissionDenied, identity.Secret, scenario.PrivateValue);
        await mcp.ErrorAsync(SqlOperationProtocol.ToolName, sql, ErrorCode.PermissionDenied, token, identity.Secret, scenario.PrivateValue);
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await administrator.GetAsync(scenario.Sentinel, token)))!,
            scenario.Sentinel, scenario.SentinelJson, 1);
    }
}
