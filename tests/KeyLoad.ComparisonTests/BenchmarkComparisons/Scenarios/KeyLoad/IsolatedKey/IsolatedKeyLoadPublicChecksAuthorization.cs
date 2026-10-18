using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004: persisted read-only authority permits reads and denies SDK, MCP and SQL writes without effects or secrets.</summary>
internal static class IsolatedKeyLoadPublicChecksAuthorization
{
    internal static async Task VerifyAsync(DistributedApplication app, KeyLoadClient administrator,
        IsolatedKeyLoadPublicChecksScenario scenario, int nodeCount, CancellationToken token)
    {
        var identity = await IsolatedKeyLoadPublicChecksIdentity.CreateAsync(administrator, scenario.Partition,
            scenario.Sentinel.Collection, Capability.DocumentsRead, token);
        using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, 1);
        var reader = new KeyLoadClient(http, identity.Secret, ComparisonClientOptions.Execution());
        await using var mcp = await IsolatedKeyLoadPublicChecksMcp.ConnectAsync(app, nodeCount, identity.Secret, token);
        var request = new GetDocumentRequest(scenario.Sentinel);
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await reader.GetAsync(scenario.Sentinel, token)))!,
            scenario.Sentinel, scenario.SentinelJson, 1);
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            await mcp.RequiredDocumentAsync(IsolatedKeyLoadPublicChecksProtocol.Get, request, token),
            scenario.Sentinel, scenario.SentinelJson, 1);
        var denied = scenario.Command(new PutDocument(scenario.Sentinel.Collection, scenario.Sentinel.Id,
            scenario.UpdatedJson, 1, ExplicitReplacement: true));
        await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(await reader.CommitAsync(denied, token),
            ErrorCode.PermissionDenied, identity.Secret, scenario.PrivateValue);
        await mcp.ErrorAsync(IsolatedKeyLoadPublicChecksProtocol.Commit, denied, ErrorCode.PermissionDenied, token,
            identity.Secret, scenario.PrivateValue);
        var sql = IsolatedKeyLoadPublicChecksProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicChecksProtocol.Commit, denied);
        await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(await reader.ExecuteSqlAsync(sql, token),
            ErrorCode.PermissionDenied, identity.Secret, scenario.PrivateValue);
        await mcp.ErrorAsync(SqlOperationProtocol.ToolName, sql, ErrorCode.PermissionDenied, token, identity.Secret, scenario.PrivateValue);
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await administrator.GetAsync(scenario.Sentinel, token)))!,
            scenario.Sentinel, scenario.SentinelJson, 1);
    }
}
