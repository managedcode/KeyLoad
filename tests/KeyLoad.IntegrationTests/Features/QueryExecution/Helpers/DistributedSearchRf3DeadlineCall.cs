using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Server;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class DistributedSearchRf3DeadlineCall
{
    internal const string DeadlineDetail = "The read execution deadline is exceeded.";
    private const string Missing = "The original distributed deadline caller has no terminal result.";
    private Task<Result<DistributedSearchPageV1>>? sdk;
    private Task<Result<JsonElement>>? sql;
    private Task<CallToolResult>? mcp;

    internal void Start(KeyLoadClient reader, McpOfficialClient official, bool useMcp, bool viaSql, CancellationToken token)
    {
        var request = DistributedSearchRf3Seed.Request();
        if (viaSql)
        {
            var call = SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, DistributedSearchProtocol.Tool, request);
            if (useMcp)
            { mcp = official.CallAsync(SqlOperationProtocol.ToolName, call, token); }
            else
            { sql = reader.ExecuteSqlAsync(call, token); }
        }
        else if (useMcp)
        { mcp = official.CallAsync(DistributedSearchProtocol.Tool, request, token); }
        else
        { sdk = reader.DistributedSearchAsync(request, token); }
    }

    internal async Task RequireOriginalAsync(Guid parentId)
    {
        if (mcp is { } originalMcp)
        {
            var actual = await originalMcp.ConfigureAwait(false);
            var id = await McpCallerAssertions.ErrorAsync(actual, ErrorCode.BudgetExceeded, dispatched: true);
            await Assert.That(id).IsEqualTo(parentId);
            var content = actual.StructuredContent ?? throw new InvalidOperationException(Missing);
            await Assert.That(content.GetProperty(McpCallerProtocol.Error).GetProperty(McpCallerProtocol.ProblemDetail).GetString())
                .IsEqualTo(DeadlineDetail);
            return;
        }
        if (sdk is { } originalSdk)
        {
            var actual = await originalSdk.ConfigureAwait(false);
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value).IsNull();
            var problem = actual.Problem ?? throw new InvalidOperationException(Missing);
            await Assert.That(problem.ErrorCode).IsEqualTo(ErrorCode.BudgetExceeded.ToString());
            await Assert.That(problem.Detail).IsEqualTo(DeadlineDetail);
            return;
        }
        var original = await (sql ?? throw new InvalidOperationException(Missing)).ConfigureAwait(false);
        await Assert.That(original.IsSuccess).IsFalse();
        await Assert.That(original.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        var error = original.Problem ?? throw new InvalidOperationException(Missing);
        await Assert.That(error.ErrorCode).IsEqualTo(ErrorCode.BudgetExceeded.ToString());
        await Assert.That(error.Detail).IsEqualTo(DeadlineDetail);
    }

    internal async Task JoinAsync(List<Exception> failures)
    {
        if (sdk is { } actualSdk)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await actualSdk.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        if (sql is { } actualSql)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await actualSql.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
        if (mcp is { } actualMcp)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await actualMcp.ConfigureAwait(false); }, failures).ConfigureAwait(false); }
    }
}
