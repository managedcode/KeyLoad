using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedSearchRf3PublicFlow
{
    private const string MissingProblem = "The distributed search refusal has no original problem.";

    internal static async Task HealthyAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, DistributedSearchRf3Seed seed,
        long destinationEpoch, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        var request = DistributedSearchRf3Seed.Request();
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.DistributedSearchAsync(request, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<DistributedSearchPageV1>(await official.CallAsync(
            DistributedSearchProtocol.Tool, request, token))).Value;
        var sql = SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, DistributedSearchProtocol.Tool, request);
        var sdkSql = await SqlRf3Protocol.SdkAsync<DistributedSearchPageV1>(reader, sql, token);
        var mcpSql = await SqlRf3Protocol.McpAsync<DistributedSearchPageV1>(official, sql, token);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
        foreach (var page in new[] { sdk, mcp, sdkSql, mcpSql })
        {
            await DistributedSearchRf3Assertions.PageAsync(page, seed,
                sourceAfter.Applied, destinationAfter.Applied, destinationEpoch);
        }
    }

    internal static async Task EmptyAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, DistributedSearchRf3Seed seed,
        long destinationEpoch, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        var original = DistributedSearchRf3Seed.Request();
        var request = original with
        {
            Search = original.Search with
            { Text = "unmatched", Vector = null, VectorField = null, Space = null }
        };
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.DistributedSearchAsync(request, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<DistributedSearchPageV1>(await official.CallAsync(
            DistributedSearchProtocol.Tool, request, token))).Value;
        var sql = SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, DistributedSearchProtocol.Tool, request);
        var sdkSql = await SqlRf3Protocol.SdkAsync<DistributedSearchPageV1>(reader, sql, token);
        var mcpSql = await SqlRf3Protocol.McpAsync<DistributedSearchPageV1>(official, sql, token);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
        foreach (var page in new[] { sdk, mcp, sdkSql, mcpSql })
        {
            await DistributedSearchRf3Assertions.EmptyPageAsync(page, seed,
                sourceAfter.Applied, destinationAfter.Applied, destinationEpoch);
        }
    }

    internal static async Task DeniedAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        var request = DistributedSearchRf3Seed.Request();
        var sdk = await reader.DistributedSearchAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(
            sdk.Problem ?? throw new InvalidOperationException(MissingProblem), JsonDefaults.Options), ErrorCode.PermissionDenied);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(DistributedSearchProtocol.Tool,
            request, token), ErrorCode.PermissionDenied, dispatched: true);
        var sql = SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, DistributedSearchProtocol.Tool, request);
        var sdkSql = await reader.ExecuteSqlAsync(sql, token);
        await Assert.That(sdkSql.IsSuccess).IsFalse();
        await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(
            sdkSql.Problem ?? throw new InvalidOperationException(MissingProblem), JsonDefaults.Options), ErrorCode.PermissionDenied);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(SqlOperationProtocol.ToolName,
            sql, token), ErrorCode.PermissionDenied, dispatched: true);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
    }
}
