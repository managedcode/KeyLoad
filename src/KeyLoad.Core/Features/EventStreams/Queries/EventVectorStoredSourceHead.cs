using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long FirstVectorSourcePosition = 1;
    private const long EmptyVectorSourceTail = 0;
    private const string MissingVectorSourceHead = "The retained event vector source head is unavailable or inconsistent.";

    private static EventSourceHead EventVectorStoredSourceHead(IKeyValueView boundedView, EventSourceRef source)
    {
        ArgumentNullException.ThrowIfNull(boundedView);
        ArgumentNullException.ThrowIfNull(source);
        EventSourceHead head;
        if (source.Kind == EventSourceKind.Topic)
        {
            var topic = boundedView.GetRecord<TopicHead>(KeySpace.Partition(PartitionRecordFamilies.TopicHead,
                source.Partition, source.Resource))
                ?? throw Errors.Fail(ErrorCode.Corruption, MissingVectorSourceHead);
            head = new(topic.TailPosition, topic.FirstAvailablePosition, topic.Generation);
        }
        else if (source.Kind == EventSourceKind.Stream)
        {
            var stream = boundedView.GetRecord<StreamHead>(KeySpace.Partition(PartitionRecordFamilies.StreamHead,
                source.Partition, source.Resource, source.StreamId))
                ?? throw Errors.Fail(ErrorCode.Corruption, MissingVectorSourceHead);
            head = new(stream.TailRevision, stream.FirstAvailableRevision, stream.Generation);
        }
        else
        { throw Errors.Fail(ErrorCode.Validation, MissingVectorSourceHead); }
        if (head.Generation != source.Generation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, MissingVectorSourceHead); }
        if (head.TailPosition < EmptyVectorSourceTail || head.FirstAvailablePosition < FirstVectorSourcePosition
            || head.FirstAvailablePosition - FirstVectorSourcePosition > head.TailPosition)
        { throw Errors.Fail(ErrorCode.Corruption, MissingVectorSourceHead); }
        return head;
    }
}
