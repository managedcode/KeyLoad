using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueDispatchCasClusterState state, CancellationToken token)
    {
        var first = QueueDispatchCasClusterProtocol.InitialVersion;
        var step = QueueDispatchCasClusterProtocol.VersionStep;
        await QueueDispatchCasClusterCurrent.RunAsync(fixture, state, first, false, token);
        foreach (var mcp in new[] { false, true })
        {
            await QueueDispatchCasClusterCommands.FailureAsync(fixture, state, Guid.NewGuid(), first,
                true, ErrorCode.PermissionDenied, first, false, mcp, state.Member.Secret, token);
            await QueueDispatchCasClusterCommands.FailureAsync(fixture, state, Guid.NewGuid(), first,
                false, ErrorCode.Validation, first, false, mcp, fixture.AdminKey, token);
            await QueueDispatchCasClusterCommands.FailureAsync(fixture, state, Guid.NewGuid(), first + step,
                true, ErrorCode.RevisionConflict, first, false, mcp, fixture.AdminKey, token);
        }
        await QueueDispatchCasClusterCommands.SuccessAsync(fixture, state, state.PauseId, first,
            true, first + step, true, false, token);
        await QueueDispatchCasClusterCommands.SuccessAsync(fixture, state, state.PauseId, first,
            true, first + step, true, true, token);
        await QueueDispatchCasClusterCommands.FailureAsync(fixture, state, state.PauseId, first,
            false, ErrorCode.Conflict, first + step, true, true, fixture.AdminKey, token);
        await QueueDispatchCasClusterCommands.FailureAsync(fixture, state, state.StaleId, first,
            false, ErrorCode.RevisionConflict, first + step, true, false, fixture.AdminKey, token);
        await QueueDispatchCasClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async (sdk, mcp) =>
        {
            var receive = new ReceiveRequest(Guid.NewGuid(), state.Lane, QueueDispatchCasClusterProtocol.OneMessage);
            await QueueLeaseClusterAssertions.DeniedAsync(await sdk.ReceiveAsync(receive, token), ErrorCode.DispatchPaused);
            await QueueDispatchCasClusterCurrent.RunAsync(fixture, state, first + step, true, token);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesReceive, receive, token),
                ErrorCode.DispatchPaused, dispatched: true);
            await QueueDispatchCasClusterCurrent.RunAsync(fixture, state, first + step, true, token);
            return true;
        }, token);
        await QueueDispatchCasClusterCurrent.RunAsync(fixture, state, first + step, true, token);
        await QueueDispatchCasClusterCommands.SuccessAsync(fixture, state, state.ResumeId, first + step,
            false, first + step + step, false, true, token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await QueueDispatchCasClusterReplay.RunAsync(fixture, state, token);
        var completed = await QueueDispatchCasClusterHealthy.RunAsync(fixture, state, token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await QueueDispatchCasClusterReplay.RunAsync(fixture, completed, token);
        await QueueDispatchCasClusterCommands.SuccessAsync(fixture, completed, Guid.NewGuid(), first + step + step,
            true, first + step + step + step, true, false, token);
        await QueueDispatchCasClusterCommands.SuccessAsync(fixture, completed, Guid.NewGuid(), first + step + step + step,
            false, first + step + step + step + step, false, true, token);
    }
}
