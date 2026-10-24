using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using static KeyLoad.IntegrationTests.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal static class StrongReadSessionResponses
{
    internal static async Task ReadAsync(RequestCqrsClusterCallers reader, EntityRef reference, CommitToken minimum,
        DocumentResult? expected, StrongReadSessionRoute route, CancellationToken token)
    {
        var request = new GetDocumentRequest(reference, minimum);
        var call = SqlClusterProtocol.Call(reference.Partition, McpCallerTools.DocumentsGet, request);
        if (route == StrongReadSessionRoute.SdkGet)
        {
            await ExactAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.GetAsync(reference, minimum, token)));
            return;
        }
        if (route == StrongReadSessionRoute.SdkCall)
        {
            await ExactAsync(expected, await SqlClusterProtocol.SdkAsync<DocumentReadResult>(reader.Sdk, call, token));
            return;
        }
        var reply = route == StrongReadSessionRoute.McpGet
            ? await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, request, token)
            : await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, call, token);
        var result = await McpCallerAssertions.SuccessAsync<DocumentReadResult>(reply);
        await ExactAsync(expected, result.Value);
        var body = reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Result);
        await Assert.That(body.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(body.EnumerateObject().Select(property => property.Name)).IsEquivalentTo(new[] { DocumentField });
        await Assert.That(body.GetProperty(DocumentField).ValueKind == JsonValueKind.Null).IsEqualTo(expected is null);
    }
    private static async Task ExactAsync(DocumentResult? expected, DocumentReadResult actual)
    {
        await Assert.That(actual).IsNotNull();
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(expected), actual);
    }
}
