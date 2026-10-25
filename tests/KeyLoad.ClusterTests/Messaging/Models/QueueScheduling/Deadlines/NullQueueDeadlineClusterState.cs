namespace KeyLoad.ClusterTests.Messaging;

internal sealed record NullQueueDeadlineClusterState(
    QueueLaneRef Lane,
    MessagingClusterIdentity Identity,
    CommandRequest Original,
    CommitReceipt Receipt,
    MessageInspection Scheduled,
    MessageInspection Future,
    AdminQueuePage OriginalPage,
    RecurringScheduleDefinition SchedulerControl,
    MessagingClusterIdentity SchedulerIdentity,
    CommandRequest SchedulerConfiguration,
    CommitReceipt SchedulerConfigurationReceipt,
    CommandRequest StopSchedulerControl);
