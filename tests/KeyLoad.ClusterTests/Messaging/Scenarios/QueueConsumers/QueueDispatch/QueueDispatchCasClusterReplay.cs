using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDispatchCasClusterReplay
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueDispatchCasClusterState state, CancellationToken token)
    {
        var initial = QueueDispatchCasClusterProtocol.InitialVersion;
        var step = QueueDispatchCasClusterProtocol.VersionStep;
        foreach (var mcpFirst in new[] { false, true })
        {
            await QueueDispatchCasClusterCommands.SuccessAsync(fixture, state, state.PauseId, initial,
                true, initial + step + step, false, mcpFirst, token);
            await QueueDispatchCasClusterCommands.SuccessAsync(fixture, state, state.ResumeId, initial + step,
                false, initial + step + step, false, mcpFirst, token);
            await QueueDispatchCasClusterCommands.FailureAsync(fixture, state, state.StaleId, initial,
                false, ErrorCode.RevisionConflict, initial + step + step, false, mcpFirst, fixture.AdminKey, token);
        }
        await QueueDispatchCasClusterCallers.UseAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, async (sdk, mcp) =>
        {
            var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(state.Original, token));
            await Assert.That(JsonDefaults.Serialize(sdkReceipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(state.Receipt))).IsTrue();
            await QueueDispatchCasClusterCurrent.RunAsync(fixture, state, initial + step + step, false, token);
            var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.DocumentsCommit, state.Original, token));
            await Assert.That(JsonDefaults.Serialize(mcpReceipt.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(state.Receipt))).IsTrue();
            await QueueDispatchCasClusterCurrent.RunAsync(fixture, state, initial + step + step, false, token);
            return true;
        }, token);
    }
}
