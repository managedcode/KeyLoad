using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicProducerRf3DocumentRefusalAssertions
{
    internal static async Task<JsonElement> RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueProducerRf3Original original, CommandRequest command, JsonElement? expected, CancellationToken token)
    {
        var reply = await sdk.CommitAsync(command, token);
        await QueueProducerRf3Assertions.DeniedAsync(reply, ErrorCode.RevisionConflict);
        await ImageAsync(sdk, mcp, original, token);
        await Assert.That(reply.Problem!.Detail).IsEqualTo(QueueProducerRf3Protocol.DocumentRevisionRefusalDetail);
        var problem = await McpAsync(mcp, original.Seed, McpCallerTools.DocumentsCommit, command, expected, token);
        await Assert.That(reply.Problem!.Type).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemType).GetString());
        await Assert.That(reply.Problem.Title).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemTitle).GetString());
        await Assert.That(reply.Problem.StatusCode).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemStatus).GetInt32());
        await Assert.That(reply.Problem.Detail).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString());
        await Assert.That(reply.Problem.ErrorCode).IsEqualTo(problem.GetProperty(McpCallerProtocol.ProblemCode).GetString());
        await ImageAsync(sdk, mcp, original, token);
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        var q1 = await sdk.ExecuteSqlAsync(sql, token);
        await QueueProducerRf3Assertions.DeniedAsync(q1, ErrorCode.RevisionConflict);
        await QueueProducerRf3Assertions.EqualAsync(q1.Problem, reply.Problem);
        await ImageAsync(sdk, mcp, original, token);
        _ = await McpAsync(mcp, original.Seed, SqlOperationProtocol.ToolName, sql, problem, token);
        await ImageAsync(sdk, mcp, original, token);
        await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, original.Seed.Original, original.Receipt, token);
        await ImageAsync(sdk, mcp, original, token);
        return problem.Clone();
    }

    private static async Task<JsonElement> McpAsync(McpOfficialClient mcp, QueueProducerRf3Seed seed, string tool,
        object request, JsonElement? expected, CancellationToken token)
    {
        var reply = await mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, seed.Identity.Secret, QueueProducerRf3Protocol.HealthyPayload);
        var problem = reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
        if (expected is { } retained)
        { await Assert.That(JsonElement.DeepEquals(problem, retained)).IsTrue(); }
        return problem.Clone();
    }

    private static async Task ImageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueProducerRf3Original original, CancellationToken token)
    {
        var seed = original.Seed;
        await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(sdk, seed,
            QueueProducerRf3Protocol.Original, token), original.Image);
        await QueueProducerRf3Assertions.PublicImageAsync(mcp, seed, QueueProducerRf3Protocol.Original, original.Image, token);
        await QueueProducerRf3Assertions.AbsentAsync(sdk, seed, QueueProducerRf3Protocol.Refused,
            QueueProducerRf3Protocol.Refused, token);
        var document = new GetDocumentRequest(QueueProducerRf3Assertions.Document(seed, QueueProducerRf3Protocol.Refused));
        var message = new InspectMessageRequest(seed.Lane, QueueProducerRf3Protocol.Refused);
        await Assert.That((await McpCallerAssertions.SuccessAsync<DocumentResult?>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, document, token))).Value).IsNull();
        await Assert.That((await McpCallerAssertions.SuccessAsync<MessageInspection?>(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, message, token))).Value).IsNull();
        await Assert.That(await SqlRf3Protocol.SdkAsync<DocumentResult?>(sdk,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet, document), token)).IsNull();
        await Assert.That(await SqlRf3Protocol.McpAsync<DocumentResult?>(mcp,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet, document), token)).IsNull();
        await Assert.That(await SqlRf3Protocol.SdkAsync<MessageInspection?>(sdk,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, message), token)).IsNull();
        await Assert.That(await SqlRf3Protocol.McpAsync<MessageInspection?>(mcp,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, message), token)).IsNull();
        await AtomicProducerRf3EventAssertions.PublicAsync(mcp, seed, QueueProducerRf3Protocol.Refused,
            new(new(QueueProducerRf3Protocol.EmptyStreamRevision, QueueProducerRf3Protocol.FirstAvailableEventRevision,
                QueueProducerRf3Protocol.InitialEventGeneration), []), token);
    }
}
