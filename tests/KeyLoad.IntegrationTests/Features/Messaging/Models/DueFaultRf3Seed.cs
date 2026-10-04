
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueFaultRf3Seed(
    PartitionRef Partition,
    QueueLaneRef RecurringLane,
    QueueLaneRef SagaLane,
    QueueLaneRef TimeoutLane,
    Guid ScheduleId,
    Guid SagaId,
    string OccurrenceId,
    string TimeoutMessageId,
    DateTimeOffset DueAt,
    RecurringScheduleDefinition Schedule,
    CommandRequest ScheduleCommand,
    CommandRequest SagaCommand);
