using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RecurringScheduleClusterAssertions
{
    private const string InspectTool = "keyload_schedule_inspect";
    private static readonly TimeSpan ProgressWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    internal static async Task WaitForOrdinalAsync(KeyLoadClient sdk, MessagingClusterScenario scenario,
        Guid scheduleId, long expectedOrdinal, CancellationToken cancellationToken)
    {
        var until = TimeProvider.System.GetUtcNow() + ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var view = (await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(
                           new(scenario.SourceQueue, scheduleId), cancellationToken))).Schedule
                       ?? throw new InvalidOperationException("The persisted schedule disappeared during autonomous progress.");
            if (view.NextOrdinal == expectedOrdinal)
            {
                return;
            }
            if (view.NextOrdinal > expectedOrdinal)
            {
                throw new InvalidOperationException("The autonomous schedule advanced beyond the expected ordinal.");
            }
            await Task.Delay(PollInterval, TimeProvider.System, cancellationToken);
        }
        throw new TimeoutException("The autonomous schedule did not advance within its bounded Orleans window.");
    }

    internal static async Task AssertScheduleAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, Guid id, long expectedRevision, long expectedGeneration,
        long expectedNextOrdinal, DateTimeOffset expectedFirstDueAt, string credential,
        CancellationToken cancellationToken)
    {
        var request = new InspectRecurringScheduleRequest(scenario.SourceQueue, id);
        var sdkView = (await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(request,
            cancellationToken))).Schedule;
        var reply = await mcp.CallAsync(InspectTool, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<RecurringScheduleReadResult>(
            reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value.Schedule).IsNotNull();
        await Assert.That(sdkView!.Definition.Lane).IsEqualTo(scenario.SourceQueue);
        await Assert.That(sdkView.Definition.ScheduleId).IsEqualTo(id);
        await Assert.That(sdkView.Cancelled).IsFalse();
        await Assert.That(sdkView.Revision).IsEqualTo(expectedRevision);
        await Assert.That(sdkView.Generation).IsEqualTo(expectedGeneration);
        await Assert.That(sdkView.NextOrdinal).IsEqualTo(expectedNextOrdinal);
        await Assert.That(sdkView.Definition.FirstDueAt).IsEqualTo(expectedFirstDueAt);
        await Assert.That(sdkView.Definition.Interval).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(sdkView.Definition.PayloadJson).IsEqualTo("{}");
        await Assert.That(sdkView.Definition.HeadersJson).IsEqualTo("{}");
        await Assert.That(sdkView.Redacted).IsTrue();
        await Assert.That(sdkView.RedactedFields).IsEquivalentTo(
            ["payload:/secret", "headers:/secret"], CollectionOrdering.Matching);
        await Assert.That(JsonDefaults.Serialize(mcpView.Value.Schedule).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingClusterScenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingClusterScenario.ProtectedHeaders);
    }

    internal static async Task AssertOccurrenceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, Guid scheduleId, long generation, long ordinal,
        DateTimeOffset dueAt, string credential, CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.SourceQueue,
            OccurrenceMessageId(scheduleId, generation, ordinal));
        var sdkView = (await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken))).Message;
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value.Message).IsNotNull();
        await Assert.That(sdkView!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(sdkView.Metadata.NotBefore).IsEqualTo(dueAt);
        await Assert.That(sdkView.PayloadJson).IsEqualTo("{}");
        await Assert.That(sdkView.HeadersJson).IsEqualTo("{}");
        await Assert.That(JsonDefaults.Serialize(mcpView.Value.Message).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingClusterScenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingClusterScenario.ProtectedHeaders);
    }

    internal static async Task AssertOccurrenceAbsentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, Guid scheduleId, long generation, long ordinal,
        CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.SourceQueue,
            OccurrenceMessageId(scheduleId, generation, ordinal));
        var sdkView = (await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken))).Message;
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(reply);
        await Assert.That(sdkView).IsNull();
        await Assert.That(mcpView.Value.Message).IsNull();
    }

    internal static string OccurrenceMessageId(Guid scheduleId, long generation, long ordinal)
        => string.Concat("recurring-", scheduleId.ToString("N"), "-",
            generation.ToString("x16", CultureInfo.InvariantCulture), "-",
            ordinal.ToString("x16", CultureInfo.InvariantCulture));
}
