using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueDispatchCasRf3State state, CancellationToken token)
    {
        var first = QueueDispatchCasRf3Protocol.InitialVersion;
        var step = QueueDispatchCasRf3Protocol.VersionStep;
        await QueueDispatchCasRf3Current.RunAsync(fixture, state, first, false, token);
        foreach (var mcp in new[] { false, true })
        {
            await QueueDispatchCasRf3Commands.FailureAsync(fixture, state, Guid.NewGuid(), first,
                true, ErrorCode.PermissionDenied, first, false, mcp, state.Member.Secret, token);
            await QueueDispatchCasRf3Commands.FailureAsync(fixture, state, Guid.NewGuid(), first,
                false, ErrorCode.Validation, first, false, mcp, fixture.AdminKey, token);
            await QueueDispatchCasRf3Commands.FailureAsync(fixture, state, Guid.NewGuid(), first + step,
                true, ErrorCode.RevisionConflict, first, false, mcp, fixture.AdminKey, token);
        }
        await QueueDispatchCasRf3Commands.SuccessAsync(fixture, state, state.PauseId, first,
            true, first + step, true, false, token);
        await QueueDispatchCasRf3Commands.SuccessAsync(fixture, state, state.PauseId, first,
            true, first + step, true, true, token);
        await QueueDispatchCasRf3Commands.FailureAsync(fixture, state, state.PauseId, first,
            false, ErrorCode.Conflict, first + step, true, true, fixture.AdminKey, token);
        await QueueDispatchCasRf3Commands.FailureAsync(fixture, state, state.StaleId, first,
            false, ErrorCode.RevisionConflict, first + step, true, false, fixture.AdminKey, token);
        await QueueDispatchCasRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async (sdk, mcp) =>
        {
            var receive = new ReceiveRequest(Guid.NewGuid(), state.Lane, QueueDispatchCasRf3Protocol.OneMessage);
            await QueueLeaseRf3Assertions.DeniedAsync(await sdk.ReceiveAsync(receive, token), ErrorCode.DispatchPaused);
            await QueueDispatchCasRf3Current.RunAsync(fixture, state, first + step, true, token);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesReceive, receive, token),
                ErrorCode.DispatchPaused, dispatched: true);
            await QueueDispatchCasRf3Current.RunAsync(fixture, state, first + step, true, token);
            return true;
        }, token);
        await QueueDispatchCasRf3Current.RunAsync(fixture, state, first + step, true, token);
        await QueueDispatchCasRf3Commands.SuccessAsync(fixture, state, state.ResumeId, first + step,
            false, first + step + step, false, true, token);
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await QueueDispatchCasRf3Replay.RunAsync(fixture, state, token);
        var completed = await QueueDispatchCasRf3Healthy.RunAsync(fixture, state, token);
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await QueueDispatchCasRf3Replay.RunAsync(fixture, completed, token);
        await QueueDispatchCasRf3Commands.SuccessAsync(fixture, completed, Guid.NewGuid(), first + step + step,
            true, first + step + step + step, true, false, token);
        await QueueDispatchCasRf3Commands.SuccessAsync(fixture, completed, Guid.NewGuid(), first + step + step + step,
            false, first + step + step + step + step, false, true, token);
    }
}
