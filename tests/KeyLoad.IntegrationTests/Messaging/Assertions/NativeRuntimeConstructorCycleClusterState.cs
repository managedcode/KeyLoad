using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class NativeRuntimeConstructorCycleClusterState
{
    private const string TimeoutPrefix = "saga-timeout-";
    private const string GuidFormat = "N";
    private const string RevisionFormat = "x16";
    private const string IdentitySeparator = "-";

    internal static async Task TerminalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, Guid id, DateTimeOffset dueAt, CancellationToken token)
    {
        var sagaRequest = new InspectSagaRequest(scenario.SourceQueue, id);
        var expectedSaga = new SagaInspection(scenario.SourceQueue, id, 2L, SagaPhase.TimedOut,
            "{}", dueAt, true, ["state:/secret"]);
        var sdkSaga = await PublicReadAssertions.SagaAsync(await sdk.InspectSagaAsync(sagaRequest, token));
        var mcpSaga = await PublicReadAssertions.SagaAsync(await mcp.CallAsync(McpCallerTools.SagaInspect, sagaRequest, token));
        await NativeRuntimeConstructorCycleClusterReceipt.SameAsync(sdkSaga, expectedSaga);
        await NativeRuntimeConstructorCycleClusterReceipt.SameAsync(mcpSaga.Value, expectedSaga);
        var messageId = string.Concat(TimeoutPrefix, id.ToString(GuidFormat), IdentitySeparator,
            1L.ToString(RevisionFormat, CultureInfo.InvariantCulture));
        var messageRequest = new InspectMessageRequest(scenario.TimeoutQueue, messageId);
        var expectedMessage = new MessageInspection(new(messageId, MessageState.Ready, 0, 1L, 1L,
            null, dueAt.AddSeconds(DueSagaClusterAssertions.TimeoutSeconds)), "{}", "{}");
        var sdkMessage = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(messageRequest, token));
        var mcpMessage = await PublicReadAssertions.MessageAsync(await mcp.CallAsync(McpCallerTools.MessagesInspect,
            messageRequest, token));
        await NativeRuntimeConstructorCycleClusterReceipt.SameAsync(sdkMessage, expectedMessage);
        await NativeRuntimeConstructorCycleClusterReceipt.SameAsync(mcpMessage.Value, expectedMessage);
    }
}
