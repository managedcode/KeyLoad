namespace KeyLoad.IntegrationTests.Messaging;

internal static class RecurringSagaClusterSupport
{
    internal const string UseGrant = "messaging.secret.use";

    internal static Task<MessagingClusterIdentity> CreateSchedulerAsync(ClusterFixture fixture,
        MessagingClusterScenario scenario, CancellationToken cancellationToken)
    {
        var sourceCapabilities = Capability.SchedulerManage | Capability.QueuePublish | Capability.QueueInspect;
        var timeoutCapabilities = sourceCapabilities | Capability.QueueConsume | Capability.QueueAck;
        ScopeGrant[] grants =
        [
            Scope(scenario.SourceQueue, sourceCapabilities),
            Scope(scenario.TimeoutQueue, timeoutCapabilities)
        ];
        return MessagingClusterIdentity.CreateAsync(fixture, scenario.SourcePartition.TenantId, grants,
            [MessagingClusterScenario.WriteGrant, UseGrant], clusterAdministrator: false, cancellationToken);
    }

    internal static RecurringScheduleDefinition Definition(MessagingClusterScenario scenario, Guid id,
        DateTimeOffset firstDue)
        => new(scenario.SourceQueue, id, firstDue, TimeSpan.FromHours(1), "UTC",
            RecurringMisfirePolicy.CatchUp, MessagingClusterScenario.ProtectedPayload,
            MessagingClusterScenario.ProtectedHeaders);

    internal static CommandRequest Command(Guid id, PartitionRef partition, Mutation mutation)
        => new(id, partition, [mutation]);

    private static ScopeGrant Scope(QueueLaneRef lane, Capability capabilities)
        => new(lane.Partition.DatabaseId, lane.Queue, capabilities);
}
