using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed record FollowerDocumentPublicObservation(FollowerDocumentReadResultV1? Value,
    Problem? Problem, CallToolResult? Official, Exception? Failure, bool IsSuccess, JsonValueKind? SqlValueKind = null)
{
    internal static async Task<FollowerDocumentPublicObservation> InvokeAsync(FollowerDocumentCaller mode,
        KeyLoadClient sdk, McpOfficialClient mcp, ReadFollowerDocumentRequestV1 request, CancellationToken token)
    {
        try
        {
            if (mode == FollowerDocumentCaller.Sdk)
            {
                var response = await sdk.ReadFollowerDocumentAsync(request, token).ConfigureAwait(false);
                return new(response.Value, response.Problem, null, null, response.IsSuccess);
            }
            var sql = SqlRf3Protocol.Call(request.Reference.Partition, FollowerDocumentRf3Protocol.Tool, request);
            if (mode == FollowerDocumentCaller.SqlSdk)
            {
                var response = await sdk.ExecuteSqlAsync(sql, token).ConfigureAwait(false);
                var value = response.IsSuccess ? response.Value.Deserialize<FollowerDocumentReadResultV1>(JsonDefaults.Options) : null;
                return new(value, response.Problem, null, null, response.IsSuccess, response.Value.ValueKind);
            }
            var official = mode == FollowerDocumentCaller.OfficialMcp
                ? await mcp.CallAsync(FollowerDocumentRf3Protocol.Tool, request, token).ConfigureAwait(false)
                : await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token).ConfigureAwait(false);
            if (official.IsError is true)
            { return new(null, null, official, null, false); }
            var result = await McpCallerAssertions.SuccessAsync<FollowerDocumentReadResultV1>(official).ConfigureAwait(false);
            return new(result.Value, null, official, null, true);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { return new(null, null, null, error, false); }
    }
}
