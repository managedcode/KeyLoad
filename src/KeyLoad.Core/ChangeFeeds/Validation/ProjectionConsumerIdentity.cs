using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ProjectionConsumerIdentity operations against the caller's database context.</summary>
public static class ProjectionConsumerIdentity
{
    internal const string InvalidConsumerIdentity = "The committed projection consumer does not match its canonical identity.";
    internal static ProjectionConsumerInfo ProjectionConsumer(IKeyValueView view, ProjectionConsumerRef consumer)
    {
        const string ProjectionConsumerDetailText = "The projection consumer is unavailable.";
        EngineResourceAccess.ValidatePartition(consumer.Partition);
        JsonData.Identifier(consumer.Name);
        var state = view.GetRecord<ProjectionConsumerInfo>(ProjectionOutbox.ConsumerKey(consumer)) ?? throw Errors.Fail(ErrorCode.NotFound, ProjectionConsumerDetailText);
        RequireProjectionConsumerIdentity(state, consumer);
        return state;
    }

    internal static void RequireProjectionConsumerIdentity(ProjectionConsumerInfo state, ProjectionConsumerRef consumer)
    {
        if (state.Consumer != consumer)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidConsumerIdentity);
        }
    }

    internal static ImmutableArray<ProjectionConsumerInfo> ProjectionConsumers(IKeyValueView view, PartitionRef partition)
    {
        const string ProjectionConsumersSpaceText = "projection-consumer";
        return view.ScanAll(KeySpace.Partition(ProjectionConsumersSpaceText, partition)).Select(record => RequireProjectionConsumerRecord(record, partition)).ToImmutableArray();
    }

    internal static ProjectionConsumerInfo RequireProjectionConsumerRecord(KeyValueRecord record, PartitionRef partition)
    {
        var state = OrleansSerialization.Deserialize<ProjectionConsumerInfo>(record.Value.Span) ?? throw Errors.Fail(ErrorCode.Corruption, InvalidConsumerIdentity);
        if (state.Consumer is null || state.Consumer.Partition != partition || !record.Key.Span.SequenceEqual(ProjectionOutbox.ConsumerKey(state.Consumer)))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidConsumerIdentity);
        }

        return state;
    }

    internal static void RequireProjectionRetentionCheckpoint(ProjectionConsumerInfo state, OutboxHead head)
    {
        const long InitialCheckpoint = 0L;
        const long RetainedPredecessorStep = 1L;
        const string InvalidCheckpointDetail = "The committed projection checkpoint is outside its retained outbox range.";
        if (state.Checkpoint < InitialCheckpoint || state.Checkpoint > head.Tail || !state.Released && state.Checkpoint < head.FirstAvailable - RetainedPredecessorStep)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCheckpointDetail);
        }
    }
}
