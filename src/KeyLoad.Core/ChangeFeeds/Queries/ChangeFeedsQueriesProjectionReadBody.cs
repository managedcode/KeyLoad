using System.Collections.Immutable;
using KeyLoad.Core.Features.ChangeFeeds;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ChangeFeedsQueriesProjectionReadBody operations against the caller's database context.</summary>
public static class ChangeFeedsQueriesProjectionReadBody
{
    internal static ProjectionBatch ReadProjectionBatchRead(this DatabaseEngine engine, IKeyValueView view, string principalId, ReadProjectionBatchRequest request)
    {
        var now = engine.Clock.GetUtcNow();
        var principal = engine.Principal(view, principalId, now);
        ProjectionBatchExecution.RequireProjectionAdministrator(principal);
        if (request.Limit < ChangeFeedsExecutionProjectionOutbox.MinimumProjectionReadCount || request.Limit > engine.Limits.MaxResults || request.MaxBytes < ChangeFeedsExecutionProjectionOutbox.MinimumProjectionReadCount || request.MaxBytes > engine.Limits.MaxProjectionBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ChangeFeedsExecutionProjectionOutbox.ProjectionBatchBudgetDetail);
        }

        var state = ChangeFeedsValidationProjectionConsumerIdentity.ProjectionConsumer(view, request.Consumer);
        if (state.Released)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ChangeFeedsExecutionProjectionOutbox.ReleasedProjectionGenerationDetail);
        }

        engine.BindProjectionReadResources(view, request.Consumer.Partition, state.Definition.Resources);
        var head = engine.ReadOutboxHead(view, request.Consumer.Partition);
        ChangeFeedsExecutionProjectionOutbox.CheckOutboxPosition(head, state.Checkpoint);
        var upper = ProjectionBatchExecution.ProjectionUpperSequence(request, state, head);
        var entries = new List<OutboxEntry>();
        var through = state.Checkpoint;
        var bytes = ChangeFeedsExecutionProjectionOutbox.InitialProjectionPageBytes;
        foreach (var stored in StoredOutboxReader.ReadRange(view, request.Consumer.Partition, through, upper, request.Limit))
        {
            var entry = stored.Entry;
            if (!ChangeFeedsExecutionProjectionOutbox.ProjectionAccepts(state.Definition, entry))
            {
                through = entry.Sequence;
                continue;
            }

            var size = stored.StoredBytes;
            if (size > request.MaxBytes - bytes)
            {
                if (through == state.Checkpoint)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, ChangeFeedsExecutionProjectionOutbox.FirstProjectionEntryExceedsBudgetDetail);
                }

                break;
            }

            entries.Add(entry);
            bytes += size;
            through = entry.Sequence;
        }

        return new(state, entries.ToImmutableArray(), through, engine.Sign(new ProjectionBatchClaims(ChangeFeedsExecutionProjectionOutbox.ProjectionBatchTokenPurpose, engine.Store.Identity.Incarnation, request.Consumer, state.Definition.IndexGeneration, state.Checkpoint, through, now.Add(engine.ChangeFeedExecution.ProjectionBatchLifetime))), through < upper);
    }

    internal static OutboxStatus GetOutboxStatusRead(this DatabaseEngine engine, IKeyValueView view, string principalId, PartitionRef partition)
    {
        ProjectionBatchExecution.RequireProjectionAdministrator(engine.Principal(view, principalId, engine.Clock.GetUtcNow()));
        EngineResourceAccess.ValidatePartition(partition);
        var head = engine.ReadOutboxHead(view, partition);
        var consumers = engine.ProjectionConsumers(view, partition);
        foreach (var consumer in consumers)
        {
            engine.BindProjectionReadResources(view, partition, consumer.Definition.Resources);
            ChangeFeedsValidationProjectionConsumerIdentity.RequireProjectionRetentionCheckpoint(consumer, head);
        }

        return new(head, consumers);
    }
}
