using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicProducerClusterDocumentRefusalAssertions
{
    internal static async Task<JsonElement> RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueProducerClusterOriginal original, CommandRequest command, JsonElement? expected, CancellationToken token)
    {
        var reply = await sdk.CommitAsync(command, token);
        await QueueProducerClusterAssertions.DeniedAsync(reply, ErrorCode.RevisionConflict);
        await ImageAsync(sdk, mcp, original, token);
        await Assert.That(reply.Problem!.Detail).IsEqualTo(QueueProducerClusterProtocol.DocumentRevisionRefusalDetail);
        var problem = await McpAsync(mcp, original.Seed, McpCallerTools.DocumentsCommit, command, expected, token);
        await Assert.That(reply.Problem!.Type).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemType).GetString());
        await Assert.That(reply.Problem.Title).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemTitle).GetString());
        await Assert.That(reply.Problem.StatusCode).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemStatus).GetInt32());
        await Assert.That(reply.Problem.Detail).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString());
        await Assert.That(reply.Problem.ErrorCode).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemCode).GetString());
        await ImageAsync(sdk, mcp, original, token);
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        var q1 = await sdk.ExecuteSqlAsync(sql, token);
        await QueueProducerClusterAssertions.DeniedAsync(q1, ErrorCode.RevisionConflict);
        await QueueProducerClusterAssertions.EqualAsync(q1.Problem, reply.Problem);
        await ImageAsync(sdk, mcp, original, token);
        _ = await McpAsync(mcp, original.Seed, SqlOperationProtocol.ToolName, sql, problem, token);
        await ImageAsync(sdk, mcp, original, token);
        await QueueProducerClusterAssertions.ReplayAsync(sdk, mcp, original.Seed.Original, original.Receipt, token);
        await ImageAsync(sdk, mcp, original, token);
        return problem.Clone();
    }

    private static async Task<JsonElement> McpAsync(McpOfficialClient mcp, QueueProducerClusterSeed seed, string tool,
        object request, JsonElement? expected, CancellationToken token)
    {
        var reply = await mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, seed.Identity.Secret, QueueProducerClusterProtocol.HealthyPayload);
        var problem = reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
        if (expected is { } retained)
        {
            await Assert.That(JsonElement.DeepEquals(problem, retained)).IsTrue();
        }
        return problem.Clone();
    }

    private static async Task ImageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueProducerClusterOriginal original, CancellationToken token)
    {
        var seed = original.Seed;
        await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(sdk, seed,
            QueueProducerClusterProtocol.Original, token), original.Image);
        await QueueProducerClusterAssertions.PublicImageAsync(mcp, seed, QueueProducerClusterProtocol.Original, original.Image, token);
        await QueueProducerClusterAssertions.AbsentAsync(sdk, seed, QueueProducerClusterProtocol.Refused,
            QueueProducerClusterProtocol.Refused, token);
        var document = new GetDocumentRequest(QueueProducerClusterAssertions.Document(seed, QueueProducerClusterProtocol.Refused));
        var message = new InspectMessageRequest(seed.Lane, QueueProducerClusterProtocol.Refused);
        await Assert.That((await PublicReadAssertions.DocumentAsync(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, document, token))).Value).IsNull();
        await Assert.That((await PublicReadAssertions.MessageAsync(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, message, token))).Value).IsNull();
        await Assert.That((await SqlClusterProtocol.SdkAsync<DocumentReadResult>(sdk,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet, document), token)).Document).IsNull();
        await Assert.That((await SqlClusterProtocol.McpAsync<DocumentReadResult>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet, document), token)).Document).IsNull();
        await Assert.That((await SqlClusterProtocol.SdkAsync<MessageInspectionResult>(sdk,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, message), token)).Message).IsNull();
        await Assert.That((await SqlClusterProtocol.McpAsync<MessageInspectionResult>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, message), token)).Message).IsNull();
        await AtomicProducerClusterEventAssertions.PublicAsync(mcp, seed, QueueProducerClusterProtocol.Refused,
            new(new(QueueProducerClusterProtocol.EmptyStreamRevision, QueueProducerClusterProtocol.FirstAvailableEventRevision,
                QueueProducerClusterProtocol.InitialEventGeneration), []), token);
    }
}
