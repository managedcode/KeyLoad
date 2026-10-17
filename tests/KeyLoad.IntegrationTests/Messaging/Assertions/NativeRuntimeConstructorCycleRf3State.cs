using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class NativeRuntimeConstructorCycleRf3State
{
    private const string TimeoutPrefix = "saga-timeout-";
    private const string GuidFormat = "N";
    private const string RevisionFormat = "x16";
    private const string IdentitySeparator = "-";

    internal static async Task TerminalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid id, DateTimeOffset dueAt, CancellationToken token)
    {
        var sagaRequest = new InspectSagaRequest(scenario.SourceQueue, id);
        var expectedSaga = new SagaInspection(scenario.SourceQueue, id, 2L, SagaPhase.TimedOut,
            "{}", dueAt, true, ["state:/secret"]);
        var sdkSaga = await PublicReadAssertions.SagaAsync(await sdk.InspectSagaAsync(sagaRequest, token));
        var mcpSaga = await PublicReadAssertions.SagaAsync(await mcp.CallAsync(McpCallerTools.SagaInspect, sagaRequest, token));
        await NativeRuntimeConstructorCycleRf3Receipt.SameAsync(sdkSaga, expectedSaga);
        await NativeRuntimeConstructorCycleRf3Receipt.SameAsync(mcpSaga.Value, expectedSaga);
        var messageId = string.Concat(TimeoutPrefix, id.ToString(GuidFormat), IdentitySeparator,
            1L.ToString(RevisionFormat, CultureInfo.InvariantCulture));
        var messageRequest = new InspectMessageRequest(scenario.TimeoutQueue, messageId);
        var expectedMessage = new MessageInspection(new(messageId, MessageState.Ready, 0, 1L, 1L,
            null, dueAt.AddSeconds(DueSagaRf3Assertions.TimeoutSeconds)), "{}", "{}");
        var sdkMessage = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(messageRequest, token));
        var mcpMessage = await PublicReadAssertions.MessageAsync(await mcp.CallAsync(McpCallerTools.MessagesInspect,
            messageRequest, token));
        await NativeRuntimeConstructorCycleRf3Receipt.SameAsync(sdkMessage, expectedMessage);
        await NativeRuntimeConstructorCycleRf3Receipt.SameAsync(mcpMessage.Value, expectedMessage);
    }
}
