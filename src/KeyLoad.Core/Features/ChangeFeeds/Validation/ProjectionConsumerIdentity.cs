using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidConsumerIdentity = "The committed projection consumer does not match its canonical identity.";

    private static ProjectionConsumerInfo ProjectionConsumer(IKeyValueView view, ProjectionConsumerRef consumer)
    {
        const string ProjectionConsumerDetailText = "The projection consumer is unavailable.";
        ValidatePartition(consumer.Partition);
        JsonData.Identifier(consumer.Name);
        var state = view.GetRecord<ProjectionConsumerInfo>(ConsumerKey(consumer))
            ?? throw Errors.Fail(ErrorCode.NotFound, ProjectionConsumerDetailText);
        RequireProjectionConsumerIdentity(state, consumer);
        return state;
    }

    private static void RequireProjectionConsumerIdentity(ProjectionConsumerInfo state, ProjectionConsumerRef consumer)
    {
        if (state.Consumer != consumer)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidConsumerIdentity); }
    }

    private ImmutableArray<ProjectionConsumerInfo> ProjectionConsumers(IKeyValueView view, PartitionRef partition)
    {
        const string ProjectionConsumersSpaceText = "projection-consumer";
        const string ProjectionConsumersDetailText = "The projection consumer limit is inconsistent.";
        var page = view.Scan(KeySpace.Partition(ProjectionConsumersSpaceText, partition), Limits.MaxProjectionConsumers);
        if (page.HasMore)
        { throw Errors.Fail(ErrorCode.Corruption, ProjectionConsumersDetailText); }
        return page.Records.Select(record => RequireProjectionConsumerRecord(record, partition)).ToImmutableArray();
    }

    private static ProjectionConsumerInfo RequireProjectionConsumerRecord(KeyValueRecord record, PartitionRef partition)
    {
        var state = NativeSerialization.Deserialize<ProjectionConsumerInfo>(record.Value.Span)
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidConsumerIdentity);
        if (state.Consumer is null || state.Consumer.Partition != partition || !record.Key.Span.SequenceEqual(ConsumerKey(state.Consumer)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidConsumerIdentity); }
        return state;
    }
}
