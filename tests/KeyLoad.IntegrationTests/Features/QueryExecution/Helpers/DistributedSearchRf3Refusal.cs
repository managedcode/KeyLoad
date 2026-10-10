using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedSearchRf3Refusal
{
    private const int ForeignVersion = 2;
    private const int OneOver = 1;
    private const string MissingProblem = "The invalid distributed request has no original problem.";

    internal static async Task RunAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, CancellationToken token)
    {
        var beforeSource = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var beforeDestination = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        var capabilities = await McpCallerAssertions.SdkSuccessAsync(await reader.QueryCapabilitiesAsync(token));
        var original = DistributedSearchRf3Seed.Request();
        var requests = new[]
        {
            original with { Version = ForeignVersion },
            original with { Search = original.Search with { Limit = checked(capabilities.MaxRows + OneOver) } }
        };
        foreach (var request in requests)
        {

            var sdk = await reader.DistributedSearchAsync(request, token);
            await Assert.That(sdk.IsSuccess).IsFalse();
            await Assert.That(sdk.Value).IsNull();
            await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(
                sdk.Problem ?? throw new InvalidOperationException(MissingProblem), JsonDefaults.Options), ErrorCode.Validation);
            await McpCallerAssertions.ErrorAsync(await official.CallAsync(DistributedSearchProtocol.Tool,
                request, token), ErrorCode.Validation, dispatched: true);
            var sql = SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, DistributedSearchProtocol.Tool, request);
            var sdkSql = await reader.ExecuteSqlAsync(sql, token);
            await Assert.That(sdkSql.IsSuccess).IsFalse();
            await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(
                sdkSql.Problem ?? throw new InvalidOperationException(MissingProblem), JsonDefaults.Options), ErrorCode.Validation);
            await McpCallerAssertions.ErrorAsync(await official.CallAsync(SqlOperationProtocol.ToolName,
                sql, token), ErrorCode.Validation, dispatched: true);
        }
        var afterSource = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var afterDestination = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(afterSource.Applied).IsEqualTo(beforeSource.Applied);
        await Assert.That(afterDestination.Applied).IsEqualTo(beforeDestination.Applied);
        await DistributedSearchRf3Documents.RequireAsync(source, destination, token).ConfigureAwait(false);
    }
}
