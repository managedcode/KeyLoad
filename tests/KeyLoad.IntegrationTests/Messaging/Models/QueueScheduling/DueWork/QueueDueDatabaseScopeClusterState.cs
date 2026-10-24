namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class QueueDueDatabaseScopeClusterState(MessagingClusterIdentity identity, DateTimeOffset due,
    QueueDueDatabaseScopeClusterLane allowed, QueueDueDatabaseScopeClusterLane denied)
{
    internal MessagingClusterIdentity Identity { get; set; } = identity;
    internal DateTimeOffset Due { get; } = due;
    internal QueueDueDatabaseScopeClusterLane Allowed { get; } = allowed;
    internal QueueDueDatabaseScopeClusterLane Denied { get; } = denied;
    internal QueueDueDatabaseScopeClusterLane[] Lanes => [Denied, Allowed];
}
