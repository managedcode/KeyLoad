using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicTriadStagedClusterReplies
{
    internal static async Task RefusedAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        AtomicTriadStagedClusterState state, CommandRequest command, ErrorCode code, string detail, string safeDetail, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ObserveAsync(root, caller, state, async () =>
        {
            var direct = await caller.Sdk.CommitAsync(command, token);
            state.Returned.Add(command.CommandId);
            await QueueProducerClusterAssertions.DeniedAsync(direct, code);
            await QueueProducerClusterAssertions.EqualAsync(direct.Problem, Errors.Problem(code, detail));
        }, failures, token);
        await ObserveAsync(root, caller, state,
            () => OfficialAsync(caller, McpCallerTools.DocumentsCommit, command, state, command.CommandId, code, safeDetail, token), failures, token);
        var request = SqlClusterProtocol.Call(state.Partition, McpCallerTools.DocumentsCommit, command);
        await ObserveAsync(root, caller, state, async () =>
        {
            var sql = await caller.Sdk.ExecuteSqlAsync(request, token);
            state.Returned.Add(command.CommandId);
            await Assert.That(sql.IsFailed).IsTrue();
            await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await QueueProducerClusterAssertions.EqualAsync(sql.Problem, Errors.Problem(code, detail));
        }, failures, token);
        await ObserveAsync(root, caller, state,
            () => OfficialAsync(caller, SqlOperationProtocol.ToolName, request, state, command.CommandId, code, safeDetail, token), failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OfficialAsync(RequestCqrsClusterCallers caller, string tool, object request,
        AtomicTriadStagedClusterState state, Guid commandId, ErrorCode code, string safeDetail, CancellationToken token)
    {
        var reply = await caller.Mcp.CallAsync(tool, request, token);
        state.Returned.Add(commandId);
        await McpCallerAssertions.ErrorAsync(reply, code, true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, AtomicTriadStagedClusterProtocol.SecondJson);
        await Assert.That(JsonElement.DeepEquals(reply.StructuredContent!.Value.GetProperty(AtomicTriadStagedClusterProtocol.ErrorProperty),
            JsonSerializer.SerializeToElement(Errors.Problem(code, safeDetail), JsonDefaults.Options))).IsTrue();
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        AtomicTriadStagedClusterState state, CancellationToken token)
    {
        if (!state.HealthyCommitted || state.Receipt is null || state.Healthy is null || !state.Returned.Contains(state.Healthy.CommandId))
        {
            throw new InvalidOperationException(AtomicTriadStagedClusterProtocol.Missing);
        }
        var failures = new List<Exception>();
        await ObserveAsync(root, caller, state, async () =>
        {
            await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.CommitAsync(state.Healthy, token)), state.Receipt);
        }, failures, token);
        await ObserveAsync(root, caller, state, async () =>
        {
            await QueueProducerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, state.Healthy, token))).Value, state.Receipt);
        }, failures, token);
        var sql = SqlClusterProtocol.Call(state.Partition, McpCallerTools.DocumentsCommit, state.Healthy);
        await ObserveAsync(root, caller, state, async () =>
        {
            await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<CommitReceipt>(caller.Sdk, sql, token), state.Receipt);
        }, failures, token);
        await ObserveAsync(root, caller, state, async () =>
        {
            await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<CommitReceipt>(caller.Mcp, sql, token), state.Receipt);
        }, failures, token);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ObserveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        AtomicTriadStagedClusterState state, Func<Task> operation, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedClusterCurrent.RunAsync(root, caller, state, token), failures);
    }
}
