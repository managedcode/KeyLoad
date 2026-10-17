using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasRf3Replay
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueDispatchCasRf3State state, CancellationToken token)
    {
        var initial = QueueDispatchCasRf3Protocol.InitialVersion;
        var step = QueueDispatchCasRf3Protocol.VersionStep;
        foreach (var mcpFirst in new[] { false, true })
        {
            await QueueDispatchCasRf3Commands.SuccessAsync(fixture, state, state.PauseId, initial,
                true, initial + step + step, false, mcpFirst, token);
            await QueueDispatchCasRf3Commands.SuccessAsync(fixture, state, state.ResumeId, initial + step,
                false, initial + step + step, false, mcpFirst, token);
            await QueueDispatchCasRf3Commands.FailureAsync(fixture, state, state.StaleId, initial,
                false, ErrorCode.RevisionConflict, initial + step + step, false, mcpFirst, fixture.AdminKey, token);
        }
        await QueueDispatchCasRf3Callers.UseAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, async (sdk, mcp) =>
        {
            var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(state.Original, token));
            await Assert.That(JsonDefaults.Serialize(sdkReceipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(state.Receipt))).IsTrue();
            await QueueDispatchCasRf3Current.RunAsync(fixture, state, initial + step + step, false, token);
            var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.DocumentsCommit, state.Original, token));
            await Assert.That(JsonDefaults.Serialize(mcpReceipt.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(state.Receipt))).IsTrue();
            await QueueDispatchCasRf3Current.RunAsync(fixture, state, initial + step + step, false, token);
            return true;
        }, token);
    }
}
