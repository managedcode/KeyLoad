using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Uses only public inspections to observe autonomous schedule progress and exact message identity.</summary>
internal static class DueRecurringRf3Assertions
{
    private const int Generation = 1;
    private const long FirstOrdinal = 0;
    private const long NextOrdinal = 1;

    internal static async Task WaitForDueTimeAsync(DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var remaining = dueAt - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }

    internal static async Task WaitForOrdinalAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        Guid scheduleId, long expectedOrdinal, CancellationToken cancellationToken)
    {
        var until = TimeProvider.System.GetUtcNow() + DueRecurringRf3Protocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var view = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(
                new(scenario.SourceQueue, scheduleId), cancellationToken));
            if (view is not null && view.NextOrdinal == expectedOrdinal)
            {
                return;
            }
            if (view is not null && view.NextOrdinal > expectedOrdinal)
            {
                throw new InvalidOperationException("The autonomous schedule advanced beyond its single due ordinal.");
            }
            await Task.Delay(DueRecurringRf3Protocol.PollInterval, cancellationToken);
        }
        throw new TimeoutException("The autonomous schedule did not advance within its bounded RF3 window.");
    }

    internal static async Task AssertScheduleAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid scheduleId, long revision, long generation, long nextOrdinal,
        bool cancelled, DateTimeOffset firstDueAt, string credential, CancellationToken cancellationToken)
    {
        var request = new InspectRecurringScheduleRequest(scenario.SourceQueue, scheduleId);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(
            request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.ScheduleInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<RecurringScheduleInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Revision).IsEqualTo(revision);
        await Assert.That(sdkView.Generation).IsEqualTo(generation);
        await Assert.That(sdkView.NextOrdinal).IsEqualTo(nextOrdinal);
        await Assert.That(sdkView.Cancelled).IsEqualTo(cancelled);
        await Assert.That(sdkView.Definition.Lane).IsEqualTo(scenario.SourceQueue);
        await Assert.That(sdkView.Definition.ScheduleId).IsEqualTo(scheduleId);
        await Assert.That(sdkView.Definition.FirstDueAt).IsEqualTo(firstDueAt);
        await Assert.That(sdkView.Definition.Interval).IsEqualTo(DueRecurringRf3Protocol.Interval);
        await Assert.That(sdkView.Definition.PayloadJson).IsEqualTo("{}");
        await Assert.That(sdkView.Definition.HeadersJson).IsEqualTo("{}");
        await Assert.That(sdkView.Redacted).IsTrue();
        await Assert.That(sdkView.RedactedFields)
            .IsEquivalentTo(["payload:/secret", "headers:/secret"], CollectionOrdering.Matching);
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }

    internal static async Task AssertOccurrenceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid scheduleId, DateTimeOffset dueAt, string credential,
        CancellationToken cancellationToken)
    {
        var id = DueRecurringRf3Protocol.OccurrenceId(scheduleId, Generation, FirstOrdinal);
        var request = new InspectMessageRequest(scenario.SourceQueue, id);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Metadata.Id).IsEqualTo(id);
        await Assert.That(sdkView.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(sdkView.Metadata.NotBefore).IsEqualTo(dueAt);
        await Assert.That(sdkView.PayloadJson).IsEqualTo("{}");
        await Assert.That(sdkView.HeadersJson).IsEqualTo("{}");
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }

    internal static async Task AssertNoOccurrenceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid scheduleId, CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.SourceQueue,
            DueRecurringRf3Protocol.OccurrenceId(scheduleId, Generation, FirstOrdinal));
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNull();
        await Assert.That(mcpView.Value).IsNull();
    }

    internal static Task AssertNoNextOccurrenceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid scheduleId, CancellationToken cancellationToken)
        => AssertMessageAbsentAsync(sdk, mcp, scenario,
            DueRecurringRf3Protocol.OccurrenceId(scheduleId, Generation, NextOrdinal), cancellationToken);

    private static async Task AssertMessageAbsentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, string id, CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.SourceQueue, id);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNull();
        await Assert.That(mcpView.Value).IsNull();
    }
}
