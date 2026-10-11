using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class RemoteDocumentSessionAuthorityRf3Assertions
{
    private const string DeniedDetail = "The principal cannot perform this operation in this scope.";
    private const string PrivateCanary = "remote-private-canary";

    internal static async Task DeniedAsync(KeyLoadClient reader, McpOfficialClient official,
        RemoteDocumentRf3Seed seed, CancellationToken token)
    {
        var request = new GetDocumentRequest(RemoteDocumentRf3Protocol.Reference, seed.Receipt.Token);
        var sdk = await reader.GetAsync(request.Reference, seed.Receipt.Token, token).ConfigureAwait(false);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await PrivacyAsync(JsonSerializer.Serialize(sdk.Problem, JsonDefaults.Options), seed.Secret);
        await Assert.That(sdk.Problem?.Detail).IsEqualTo(DeniedDetail);
        await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(sdk.Problem,
            JsonDefaults.Options), ErrorCode.PermissionDenied);
        await OfficialAsync(await official.CallAsync(RemoteDocumentRf3Protocol.DocumentsGet, request, token), seed.Secret);
        var call = SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition,
            RemoteDocumentRf3Protocol.DocumentsGet, request);
        var sql = await reader.ExecuteSqlAsync(call, token).ConfigureAwait(false);
        await Assert.That(sql.IsSuccess).IsFalse();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await PrivacyAsync(JsonSerializer.Serialize(sql.Problem, JsonDefaults.Options), seed.Secret);
        await Assert.That(sql.Problem?.Detail).IsEqualTo(DeniedDetail);
        await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(sql.Problem,
            JsonDefaults.Options), ErrorCode.PermissionDenied);
        await OfficialAsync(await official.CallAsync(SqlOperationProtocol.ToolName, call, token), seed.Secret);
    }

    private static async Task OfficialAsync(CallToolResult actual, string secret)
    {
        await McpCallerAssertions.ErrorAsync(actual, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(actual, secret, PrivateCanary);
        await McpCallerAssertions.DoesNotDiscloseAsync(actual, secret, RemoteDocumentRf3Protocol.OriginalJson);
        await Assert.That(actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(DeniedDetail);
    }

    private static async Task PrivacyAsync(string actual, string secret)
    {
        await Assert.That(actual.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(actual.Contains(PrivateCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(actual.Contains(RemoteDocumentRf3Protocol.OriginalJson, StringComparison.Ordinal)).IsFalse();
    }
}
