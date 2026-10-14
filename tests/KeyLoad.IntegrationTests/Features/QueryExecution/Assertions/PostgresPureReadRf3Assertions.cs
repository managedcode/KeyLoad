using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPureReadRf3Assertions
{
    internal static async Task HealthyAsync(RequestCqrsRf3Callers callers, PostgresRf3Caller wire,
        PostgresPureReadRf3State state, CancellationToken token)
    {
        await PostgresCompositionRf3Models.LiteralAsync(state.Image, reverse: true);
        await SqlRf3Protocol.EqualAsync(state.Image, await PostgresCompositionRf3Models.ReadAsync(callers.Sdk, token));
        foreach (var prepared in new[] { false, true })
        {
            await DocumentAsync(callers, wire, PostgresRf3State.Left, state.Image.Left, prepared, token);
            await DocumentAsync(callers, wire, PostgresRf3State.Right, state.Image.Right, prepared, token);
            await DocumentAsync(callers, wire, PostgresRf3State.Left with { Id = PostgresPureReadRf3Protocol.Missing }, null, prepared, token);
            await MessageAsync(callers, wire, PostgresCompositionRf3Protocol.SourceQueue,
                PostgresCompositionRf3Protocol.Message, state.Image.Source, prepared, token);
            await MessageAsync(callers, wire, PostgresCompositionRf3Protocol.TargetQueue,
                PostgresPureReadRf3Protocol.TargetId, state.Image.Target, prepared, token);
            await MessageAsync(callers, wire, PostgresCompositionRf3Protocol.TargetQueue,
                PostgresPureReadRf3Protocol.Missing, null, prepared, token);
            var request = PostgresPureReadRf3Protocol.Graph();
            await SqlRf3Protocol.EqualAsync(state.Image.Graph, await wire.ExecuteAsync<global::KeyLoad.GraphTraversal>(
                PostgresPureReadRf3Protocol.GraphSql, PostgresPureReadRf3Protocol.Parameters(request), prepared, token));
            await SqlRf3Protocol.EqualAsync(state.Image.Graph, await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.TraverseAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(state.Image.Graph, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
                await callers.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token))).Value);
        }
    }

    private static async Task DocumentAsync(RequestCqrsRf3Callers callers, PostgresRf3Caller wire,
        EntityRef reference, DocumentResult? expected, bool prepared, CancellationToken token)
    {
        var request = new GetDocumentRequest(reference);
        await SqlRf3Protocol.EqualAsync(new DocumentReadResult(expected), await wire.ExecuteAsync<DocumentReadResult>(
            PostgresPureReadRf3Protocol.DocumentSql, PostgresPureReadRf3Protocol.Parameters(request), prepared, token));
        await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(reference, token)));
        var official = await callers.Mcp.CallAsync(McpToolNames.DocumentsGet, request, token);
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(official)).Value);
    }

    private static async Task MessageAsync(RequestCqrsRf3Callers callers, PostgresRf3Caller wire,
        string queue, string id, MessageInspection? expected, bool prepared, CancellationToken token)
    {
        var request = PostgresPureReadRf3Protocol.Message(queue, id);
        await SqlRf3Protocol.EqualAsync(new MessageInspectionResult(expected), await wire.ExecuteAsync<MessageInspectionResult>(
            PostgresPureReadRf3Protocol.MessageSql, PostgresPureReadRf3Protocol.Parameters(request), prepared, token));
        await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.MessageAsync(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value);
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers callers, PostgresRf3Caller wire, CancellationToken token)
    {
        var request = PostgresPureReadRf3Protocol.Graph();
        var sdk = await callers.Sdk.TraverseAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token),
            ErrorCode.PermissionDenied, dispatched: true);
        await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(PostgresPureReadRf3Protocol.GraphSql,
            PostgresPureReadRf3Protocol.Parameters(request), prepared: true, token),
            PostgresRf3Protocol.PermissionSqlState, ErrorCode.PermissionDenied);
    }
}
