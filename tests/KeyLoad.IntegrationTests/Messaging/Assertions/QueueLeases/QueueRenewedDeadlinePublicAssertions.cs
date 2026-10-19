using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueRenewedDeadlinePublicAssertions
{
    private const string Stale = "The queue deadline condition no longer matches.";
    private const string NotDue = "The queue deadline has not been reached.";
    private const string GenericMcpDetail = "The database operation could not be completed.";

    internal static string Detail(ErrorCode code)
        => code == ErrorCode.RevisionConflict ? Stale : code == ErrorCode.Conflict
            ? NotDue : throw new InvalidOperationException("The deadline refusal is not a selected case.");

    internal static async Task ImagesAsync(RequestCqrsClusterCallers callers, QueueLeaseClusterSeed seed,
        MessageInspection expected, CancellationToken token)
    {
        await QueueLeaseClusterAssertions.EqualAsync(
            await QueueLeaseClusterAssertions.InspectAsync(callers.Sdk, seed.Lane, token), expected);
        var reply = await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesInspect,
                new InspectMessageRequest(seed.Lane, QueueLeaseClusterProtocol.Message), token));
        await QueueLeaseClusterAssertions.EqualAsync(reply.Value, new MessageInspectionResult(expected));
        await QueueLeaseClusterFuture.RequireAsync(callers.Sdk, callers.Mcp, seed, token);
    }

    internal static async Task<string> RefusalAsync(RequestCqrsClusterCallers callers,
        CommandRequest request, ErrorCode code, string detail, CancellationToken token)
    {
        var failure = await callers.Sdk.CommitAsync(request, token);
        await Assert.That(failure.IsFailed).IsTrue();
        await Assert.That(failure.Value).IsNull();
        await Assert.That(failure.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(failure.Problem.Detail).IsEqualTo(detail);
        var reply = await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token);
        await McpCallerAssertions.ErrorAsync(reply, code, true);
        var envelope = reply.StructuredContent
            ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult);
        var problem = envelope.GetProperty(McpCallerProtocol.Error);
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemType).GetString())
            .IsEqualTo("urn:keyload:error:" + code);
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemTitle).GetString()).IsEqualTo(code.ToString());
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(GenericMcpDetail);
        return JsonSerializer.Serialize(failure.Problem, JsonDefaults.Options);
    }

    internal static async Task RefusalsAsync(RequestCqrsClusterCallers callers,
        QueueRenewedDeadlinePublicState state, MessageInspection expected, CancellationToken token)
    {
        foreach (var original in state.Refusals)
        {
            await Assert.That(await RefusalAsync(callers, original.Request, original.Code, original.Detail, token))
                .IsEqualTo(original.OriginalProblem);
            await ImagesAsync(callers, state.Seed, expected, token);
        }
    }

    internal static async Task LiveOriginalAsync(RequestCqrsClusterCallers callers,
        QueueRenewedDeadlinePublicState state, CancellationToken token)
    {
        await Assert.That(TimeProvider.System.GetUtcNow()).IsLessThan(state.Renewed.Metadata.LeaseUntil!.Value);
        await QueueLeaseClusterAssertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(state.Request, token)), state.Claim);
        var original = await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, state.Request, token));
        await QueueLeaseClusterAssertions.EqualAsync(original.Value, state.Claim);
        await QueueLeaseClusterAssertions.ReplayedAsync(callers.Sdk, callers.Mcp, state.Renew, state.RenewReceipt, token);
        await ImagesAsync(callers, state.Seed, state.Renewed, token);
    }

    internal static async Task SettledOriginalAsync(RequestCqrsClusterCallers callers,
        QueueRenewedDeadlineSettled state, CancellationToken token)
    {
        var original = state.Original;
        await QueueLeaseClusterAssertions.DeniedAsync(
            await callers.Sdk.ReceiveAsync(original.Request, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, original.Request, token), ErrorCode.StaleLease, true);
        await QueueLeaseClusterAssertions.ReplayedAsync(callers.Sdk, callers.Mcp,
            original.Renew, original.RenewReceipt, token);
        await QueueLeaseClusterAssertions.ReplayedAsync(callers.Sdk, callers.Mcp, state.Ack, state.AckReceipt, token);
        await RefusalsAsync(callers, original, state.Acked, token);
        await ImagesAsync(callers, original.Seed, state.Acked, token);
    }

    internal static async Task ReceiptAsync(CommitReceipt actual, Guid id, QueueLaneRef lane,
        string message, string action, long version)
    {
        await Assert.That(actual.CommandId).IsEqualTo(id);
        var mutation = await Assert.That(actual.Mutations).HasSingleItem();
        await QueueLeaseClusterAssertions.EqualAsync(mutation, new MutationReceipt(action, lane.Queue, message, version));
    }
}
