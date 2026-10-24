using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using static KeyLoad.IntegrationTests.StorageReplication.DocumentSessionReadClusterProtocol;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal static class DocumentSessionReadClusterAssertions
{
    internal static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient mcp, EntityRef reference,
        EntityRef stored, CommitToken minimum, string json, long revision, CancellationToken token)
    {
        var actual = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, minimum, token));
        var official = await PublicReadAssertions.DocumentAsync(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token));
        await LiteralAsync(actual, stored, json, revision);
        await LiteralAsync(official.Value, stored, json, revision);
        var call = SqlClusterProtocol.Call(reference.Partition, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum));
        await LiteralAsync((await SqlClusterProtocol.SdkAsync<DocumentReadResult>(sdk, call, token)).Document, stored, json, revision);
        await LiteralAsync((await SqlClusterProtocol.McpAsync<DocumentReadResult>(mcp, call, token)).Document, stored, json, revision);
    }

    internal static async Task RejectedAsync(KeyLoadClient sdk, McpOfficialClient mcp, EntityRef reference,
        EntityRef stored, CommitToken minimum, string credential, CancellationToken token)
    {
        (CommitToken Token, string Detail)[] invalid =
        [
            (minimum with { Incarnation = Guid.NewGuid() }, WrongIncarnation),
            (minimum with { AtomicPartitionId = Guid.NewGuid().ToString() }, OutOfScope),
            (minimum with { Position = long.MaxValue }, Future),
            (minimum with { Position = Zero }, Invalid)
        ];
        foreach (var failure in invalid)
        {
            var sdkFailure = await sdk.GetAsync(reference, failure.Token, token);
            await Assert.That(sdkFailure.IsSuccess).IsFalse();
            await Assert.That(sdkFailure.Value).IsNull();
            await Assert.That(sdkFailure.Problem?.StatusCode).IsEqualTo(400);
            await Assert.That(sdkFailure.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
            await Assert.That(sdkFailure.Problem?.Detail).IsEqualTo(failure.Detail);
            var official = await mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, failure.Token), token);
            await McpCallerAssertions.ErrorAsync(official, ErrorCode.TokenInvalidated, dispatched: true);
            await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(failure.Detail);
            await McpCallerAssertions.DoesNotDiscloseAsync(official, credential, FirstJson);
            var call = SqlClusterProtocol.Call(reference.Partition, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, failure.Token));
            var sql = await sdk.ExecuteSqlAsync(call, token);
            await Assert.That(sql.IsSuccess).IsFalse();
            await Assert.That(sql.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
            await Assert.That(sql.Problem?.StatusCode).IsEqualTo(400);
            await Assert.That(sql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
            await Assert.That(sql.Problem?.Detail).IsEqualTo(failure.Detail);
            var sqlOfficial = await mcp.CallAsync(SqlOperationProtocol.ToolName, call, token);
            await McpCallerAssertions.ErrorAsync(sqlOfficial, ErrorCode.TokenInvalidated, dispatched: true);
            await Assert.That(sqlOfficial.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(failure.Detail);
            await McpCallerAssertions.DoesNotDiscloseAsync(sqlOfficial, credential, FirstJson);
            await HealthyAsync(sdk, mcp, reference, stored, minimum, FirstJson, First, token);
        }
    }

    private static async Task LiteralAsync(DocumentResult? actual, EntityRef reference, string json, long revision)
    {
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Reference).IsEqualTo(reference);
        await Assert.That(actual.Revision).IsEqualTo(revision);
        await Assert.That(actual.Json).IsEqualTo(json);
        await Assert.That(actual.Redacted).IsFalse();
        await Assert.That(actual.RedactedFields).IsEmpty();
    }
}
