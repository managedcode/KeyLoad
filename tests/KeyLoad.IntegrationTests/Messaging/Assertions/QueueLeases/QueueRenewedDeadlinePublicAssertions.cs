using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueRenewedDeadlinePublicAssertions
{
    private const string Stale = "The queue deadline condition no longer matches.";
    private const string NotDue = "The queue deadline has not been reached.";
    private const string GenericMcpDetail = "The database operation could not be completed.";

    internal static string Detail(ErrorCode code)
        => code == ErrorCode.RevisionConflict ? Stale : code == ErrorCode.Conflict
            ? NotDue : throw new InvalidOperationException("The deadline refusal is not a selected case.");

    internal static async Task ImagesAsync(RequestCqrsRf3Callers callers, QueueLeaseRf3Seed seed,
        MessageInspection expected, CancellationToken token)
    {
        await QueueLeaseRf3Assertions.EqualAsync(
            await QueueLeaseRf3Assertions.InspectAsync(callers.Sdk, seed.Lane, token), expected);
        var reply = await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesInspect,
                new InspectMessageRequest(seed.Lane, QueueLeaseRf3Protocol.Message), token));
        await QueueLeaseRf3Assertions.EqualAsync(reply.Value, new MessageInspectionResult(expected));
        await QueueLeaseRf3Future.RequireAsync(callers.Sdk, callers.Mcp, seed, token);
    }

    internal static async Task<string> RefusalAsync(RequestCqrsRf3Callers callers,
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

    internal static async Task RefusalsAsync(RequestCqrsRf3Callers callers,
        QueueRenewedDeadlinePublicState state, MessageInspection expected, CancellationToken token)
    {
        foreach (var original in state.Refusals)
        {
            await Assert.That(await RefusalAsync(callers, original.Request, original.Code, original.Detail, token))
                .IsEqualTo(original.OriginalProblem);
            await ImagesAsync(callers, state.Seed, expected, token);
        }
    }

    internal static async Task LiveOriginalAsync(RequestCqrsRf3Callers callers,
        QueueRenewedDeadlinePublicState state, CancellationToken token)
    {
        await Assert.That(TimeProvider.System.GetUtcNow()).IsLessThan(state.Renewed.Metadata.LeaseUntil!.Value);
        await QueueLeaseRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(state.Request, token)), state.Claim);
        var original = await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, state.Request, token));
        await QueueLeaseRf3Assertions.EqualAsync(original.Value, state.Claim);
        await QueueLeaseRf3Assertions.ReplayedAsync(callers.Sdk, callers.Mcp, state.Renew, state.RenewReceipt, token);
        await ImagesAsync(callers, state.Seed, state.Renewed, token);
    }

    internal static async Task SettledOriginalAsync(RequestCqrsRf3Callers callers,
        QueueRenewedDeadlineSettled state, CancellationToken token)
    {
        var original = state.Original;
        await QueueLeaseRf3Assertions.DeniedAsync(
            await callers.Sdk.ReceiveAsync(original.Request, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, original.Request, token), ErrorCode.StaleLease, true);
        await QueueLeaseRf3Assertions.ReplayedAsync(callers.Sdk, callers.Mcp,
            original.Renew, original.RenewReceipt, token);
        await QueueLeaseRf3Assertions.ReplayedAsync(callers.Sdk, callers.Mcp, state.Ack, state.AckReceipt, token);
        await RefusalsAsync(callers, original, state.Acked, token);
        await ImagesAsync(callers, original.Seed, state.Acked, token);
    }

    internal static async Task ReceiptAsync(CommitReceipt actual, Guid id, QueueLaneRef lane,
        string message, string action, long version)
    {
        await Assert.That(actual.CommandId).IsEqualTo(id);
        var mutation = await Assert.That(actual.Mutations).HasSingleItem();
        await QueueLeaseRf3Assertions.EqualAsync(mutation, new MutationReceipt(action, lane.Queue, message, version));
    }
}
