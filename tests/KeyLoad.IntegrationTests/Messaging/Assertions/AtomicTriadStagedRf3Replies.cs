using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicTriadStagedRf3Replies
{
    internal static async Task RefusedAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        AtomicTriadStagedRf3State state, CommandRequest command, ErrorCode code, string detail, string safeDetail, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ObserveAsync(root, caller, state, async () =>
        {
            var direct = await caller.Sdk.CommitAsync(command, token);
            state.Returned.Add(command.CommandId);
            await QueueProducerRf3Assertions.DeniedAsync(direct, code);
            await QueueProducerRf3Assertions.EqualAsync(direct.Problem, Errors.Problem(code, detail));
        }, failures, token);
        await ObserveAsync(root, caller, state,
            () => OfficialAsync(caller, McpCallerTools.DocumentsCommit, command, state, command.CommandId, code, safeDetail, token), failures, token);
        var request = SqlRf3Protocol.Call(state.Partition, McpCallerTools.DocumentsCommit, command);
        await ObserveAsync(root, caller, state, async () =>
        {
            var sql = await caller.Sdk.ExecuteSqlAsync(request, token);
            state.Returned.Add(command.CommandId);
            await Assert.That(sql.IsFailed).IsTrue();
            await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await QueueProducerRf3Assertions.EqualAsync(sql.Problem, Errors.Problem(code, detail));
        }, failures, token);
        await ObserveAsync(root, caller, state,
            () => OfficialAsync(caller, SqlOperationProtocol.ToolName, request, state, command.CommandId, code, safeDetail, token), failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OfficialAsync(RequestCqrsRf3Callers caller, string tool, object request,
        AtomicTriadStagedRf3State state, Guid commandId, ErrorCode code, string safeDetail, CancellationToken token)
    {
        var reply = await caller.Mcp.CallAsync(tool, request, token);
        state.Returned.Add(commandId);
        await McpCallerAssertions.ErrorAsync(reply, code, true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, AtomicTriadStagedRf3Protocol.SecondJson);
        await Assert.That(JsonElement.DeepEquals(reply.StructuredContent!.Value.GetProperty(AtomicTriadStagedRf3Protocol.ErrorProperty),
            JsonSerializer.SerializeToElement(Errors.Problem(code, safeDetail), JsonDefaults.Options))).IsTrue();
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        AtomicTriadStagedRf3State state, CancellationToken token)
    {
        if (!state.HealthyCommitted || state.Receipt is null || state.Healthy is null || !state.Returned.Contains(state.Healthy.CommandId))
        {
            throw new InvalidOperationException(AtomicTriadStagedRf3Protocol.Missing);
        }
        var failures = new List<Exception>();
        await ObserveAsync(root, caller, state, async () =>
        {
            await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.CommitAsync(state.Healthy, token)), state.Receipt);
        }, failures, token);
        await ObserveAsync(root, caller, state, async () =>
        {
            await QueueProducerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, state.Healthy, token))).Value, state.Receipt);
        }, failures, token);
        var sql = SqlRf3Protocol.Call(state.Partition, McpCallerTools.DocumentsCommit, state.Healthy);
        await ObserveAsync(root, caller, state, async () =>
        {
            await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<CommitReceipt>(caller.Sdk, sql, token), state.Receipt);
        }, failures, token);
        await ObserveAsync(root, caller, state, async () =>
        {
            await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<CommitReceipt>(caller.Mcp, sql, token), state.Receipt);
        }, failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ObserveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        AtomicTriadStagedRf3State state, Func<Task> operation, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedRf3Current.RunAsync(root, caller, state, token), failures);
    }
}
