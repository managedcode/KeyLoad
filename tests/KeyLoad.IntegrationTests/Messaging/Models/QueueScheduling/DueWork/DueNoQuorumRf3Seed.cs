
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueNoQuorumRf3Seed(
    PartitionRef Partition,
    QueueLaneRef Lane,
    Guid ScheduleId,
    string OccurrenceId,
    string NextOccurrenceId,
    DateTimeOffset DueAt,
    RecurringScheduleDefinition Definition,
    DueNoQuorumRf3Creator Creator)
{
    public override string ToString() => "DueNoQuorumRf3Seed(<private>)";
}
