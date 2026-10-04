using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

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
        => new(scenario.SourceQueue, id, firstDue, TimeSpan.FromSeconds(1), "UTC",
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

    internal static async Task AssertScheduleAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid id, long expectedRevision, long expectedGeneration,
        long expectedNextOrdinal, CancellationToken cancellationToken)
    {
        var request = new InspectRecurringScheduleRequest(scenario.SourceQueue, id);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(request,
            cancellationToken));
        var mcpView = await McpCallerAssertions.SuccessAsync<RecurringScheduleInspection?>(
            await mcp.CallAsync(InspectTool, request, cancellationToken));
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(sdkView!.Revision).IsEqualTo(expectedRevision);
        await Assert.That(sdkView.Generation).IsEqualTo(expectedGeneration);
        await Assert.That(sdkView.NextOrdinal).IsEqualTo(expectedNextOrdinal);
        await Assert.That(mcpView.Value).IsEqualTo(sdkView);
    }

    internal static async Task AssertOccurrenceAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        Guid scheduleId, long ordinal, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var id = OccurrenceMessageId(scheduleId, generation: 2, ordinal);
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.SourceQueue, id), cancellationToken));
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(result.Metadata.NotBefore).IsEqualTo(dueAt);
    }

    internal static string OccurrenceMessageId(Guid scheduleId, long generation, long ordinal)
        => string.Concat("recurring-", scheduleId.ToString("N"), "-",
            generation.ToString("x16", CultureInfo.InvariantCulture), "-",
            ordinal.ToString("x16", CultureInfo.InvariantCulture));
}
