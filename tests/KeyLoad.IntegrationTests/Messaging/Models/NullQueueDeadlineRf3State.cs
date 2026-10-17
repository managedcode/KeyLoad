namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record NullQueueDeadlineRf3State(
    QueueLaneRef Lane,
    MessagingRf3Identity Identity,
    CommandRequest Original,
    CommitReceipt Receipt,
    MessageInspection Scheduled,
    MessageInspection Future,
    AdminQueuePage OriginalPage,
    RecurringScheduleDefinition SchedulerControl,
    MessagingRf3Identity SchedulerIdentity,
    CommandRequest SchedulerConfiguration,
    CommitReceipt SchedulerConfigurationReceipt,
    CommandRequest StopSchedulerControl);
