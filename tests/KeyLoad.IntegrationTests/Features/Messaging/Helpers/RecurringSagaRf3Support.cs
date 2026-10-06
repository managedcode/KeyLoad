using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RecurringSagaRf3Support
{
    internal const string UseGrant = "messaging.secret.use";

    internal static Task<MessagingRf3Identity> CreateSchedulerAsync(ClusterFixture fixture,
        MessagingRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var sourceCapabilities = Capability.SchedulerManage | Capability.QueuePublish | Capability.QueueInspect;
        var timeoutCapabilities = sourceCapabilities | Capability.QueueConsume | Capability.QueueAck;
        ScopeGrant[] grants =
        [
            Scope(scenario.SourceQueue, sourceCapabilities),
            Scope(scenario.TimeoutQueue, timeoutCapabilities)
        ];
        return MessagingRf3Identity.CreateAsync(fixture, scenario.SourcePartition.TenantId, grants,
            [MessagingRf3Scenario.WriteGrant, UseGrant], clusterAdministrator: false, cancellationToken);
    }

    internal static RecurringScheduleDefinition Definition(MessagingRf3Scenario scenario, Guid id,
        DateTimeOffset firstDue)
        => new(scenario.SourceQueue, id, firstDue, TimeSpan.FromHours(1), "UTC",
            RecurringMisfirePolicy.CatchUp, MessagingRf3Scenario.ProtectedPayload,
            MessagingRf3Scenario.ProtectedHeaders);

    internal static CommandRequest Command(Guid id, PartitionRef partition, Mutation mutation)
        => new(id, partition, [mutation]);

    private static ScopeGrant Scope(QueueLaneRef lane, Capability capabilities)
        => new(lane.Partition.DatabaseId, lane.Queue, capabilities);
}

internal static class RecurringScheduleRf3Assertions
{
    private const string InspectTool = "keyload_schedule_inspect";
    private static readonly TimeSpan ProgressWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    internal static async Task WaitForOrdinalAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        Guid scheduleId, long expectedOrdinal, CancellationToken cancellationToken)
    {
        var until = TimeProvider.System.GetUtcNow() + ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var view = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(
                new(scenario.SourceQueue, scheduleId), cancellationToken))
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
        throw new TimeoutException("The autonomous schedule did not advance within its bounded RF3 window.");
    }

    internal static async Task AssertScheduleAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid id, long expectedRevision, long expectedGeneration,
        long expectedNextOrdinal, DateTimeOffset expectedFirstDueAt, string credential,
        CancellationToken cancellationToken)
    {
        var request = new InspectRecurringScheduleRequest(scenario.SourceQueue, id);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(request,
            cancellationToken));
        var reply = await mcp.CallAsync(InspectTool, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<RecurringScheduleInspection?>(
            reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
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
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }

    internal static async Task AssertOccurrenceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid scheduleId, long generation, long ordinal,
        DateTimeOffset dueAt, string credential, CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.SourceQueue,
            OccurrenceMessageId(scheduleId, generation, ordinal));
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(sdkView.Metadata.NotBefore).IsEqualTo(dueAt);
        await Assert.That(sdkView.PayloadJson).IsEqualTo("{}");
        await Assert.That(sdkView.HeadersJson).IsEqualTo("{}");
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }

    internal static async Task AssertOccurrenceAbsentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid scheduleId, long generation, long ordinal,
        CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.SourceQueue,
            OccurrenceMessageId(scheduleId, generation, ordinal));
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNull();
        await Assert.That(mcpView.Value).IsNull();
    }

    internal static string OccurrenceMessageId(Guid scheduleId, long generation, long ordinal)
        => string.Concat("recurring-", scheduleId.ToString("N"), "-",
            generation.ToString("x16", CultureInfo.InvariantCulture), "-",
            ordinal.ToString("x16", CultureInfo.InvariantCulture));
}
