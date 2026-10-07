using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.DocumentSessionReadRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class DocumentSessionReadRf3Assertions
{
    internal static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient mcp, EntityRef reference,
        CommitToken minimum, string json, long revision, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, minimum, token));
        var official = await McpCallerAssertions.SuccessAsync<DocumentResult>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token));
        await LiteralAsync(actual, reference, json, revision);
        await LiteralAsync(official.Value, reference, json, revision);
        var call = SqlRf3Protocol.Call(reference.Partition, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum));
        await LiteralAsync(await SqlRf3Protocol.SdkAsync<DocumentResult>(sdk, call, token), reference, json, revision);
        await LiteralAsync(await SqlRf3Protocol.McpAsync<DocumentResult>(mcp, call, token), reference, json, revision);
    }

    internal static async Task RejectedAsync(KeyLoadClient sdk, McpOfficialClient mcp, EntityRef reference,
        CommitToken minimum, string credential, CancellationToken token)
    {
        (CommitToken Token, string Detail)[] invalid =
        [
            (minimum with { Incarnation = Guid.NewGuid() }, WrongIncarnation),
            (minimum with { AtomicPartitionId = Guid.NewGuid().ToString() }, OutOfScope),
            (minimum with { OwnershipEpoch = minimum.OwnershipEpoch + First }, OutOfScope),
            (minimum with { Position = long.MaxValue }, Future),
            (minimum with { Position = Zero }, Invalid)
        ];
        foreach (var failure in invalid)
        {
            var sdkFailure = await sdk.GetAsync(reference, failure.Token, token);
            await Assert.That(sdkFailure.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
            await Assert.That(sdkFailure.Problem?.Detail).IsEqualTo(failure.Detail);
            var official = await mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, failure.Token), token);
            await McpCallerAssertions.ErrorAsync(official, ErrorCode.TokenInvalidated, dispatched: true);
            await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(failure.Detail);
            await McpCallerAssertions.DoesNotDiscloseAsync(official, credential, FirstJson);
            var call = SqlRf3Protocol.Call(reference.Partition, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, failure.Token));
            var sql = await sdk.ExecuteSqlAsync(call, token);
            await Assert.That(sql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
            await Assert.That(sql.Problem?.Detail).IsEqualTo(failure.Detail);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, call, token),
                ErrorCode.TokenInvalidated, dispatched: true);
            await HealthyAsync(sdk, mcp, reference, minimum, FirstJson, First, token);
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
