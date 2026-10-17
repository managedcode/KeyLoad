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
