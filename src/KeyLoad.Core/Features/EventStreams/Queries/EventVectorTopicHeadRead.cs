using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorTopicHeadRead
{
    private const int PresenceRecord = 1;
    private const string MissingRequiredHead = "The canonical topic records require a persisted source head.";

    // The owning coverage query authenticates the scope and resource before calling this reader.
    internal static (TopicHead? Head, EventVectorCoverageRow? Row) Read(IKeyValueView boundedView,
        PartitionRef partition, string topic, EventVectorInventoryReadBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundedView);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();
        var key = KeySpace.Partition(PartitionRecordFamilies.TopicHead, partition, topic);
        TopicHead? head = null;
        EventVectorCoverageRow? row = null;
        boundedView.ReadValue(key, value =>
        {
            head = NativeSerialization.Deserialize<TopicHead>(value);
            row = new EventVectorCoverageRow { Key = key, Value = value.ToArray() };
        }, budget.Observe);
        if (row is not null)
        {
            return (head ?? throw Errors.Fail(ErrorCode.Corruption, MissingRequiredHead), row);
        }
        RequireAbsent(boundedView, partition, topic, PartitionRecordFamilies.TopicEvent,
            budget, cancellationToken);
        RequireAbsent(boundedView, partition, topic, PartitionRecordFamilies.TopicEventIdentity,
            budget, cancellationToken);
        return (null, null);
    }

    private static void RequireAbsent(IKeyValueView view, PartitionRef partition, string topic,
        string family, EventVectorInventoryReadBudget budget, CancellationToken cancellationToken)
    {
        var found = false;
        view.VisitRange(KeySpace.Partition(family, partition, topic), PresenceRecord,
            (_, _) => { found = true; return false; }, observer: budget.Observe,
            cancellationToken: cancellationToken);
        if (found)
        { throw Errors.Fail(ErrorCode.Corruption, MissingRequiredHead); }
    }
}
