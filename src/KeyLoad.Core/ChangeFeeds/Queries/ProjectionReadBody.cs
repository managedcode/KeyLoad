using System.Collections.Immutable;
using KeyLoad.Core.ChangeFeeds;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ProjectionReadBody operations against the caller's database context.</summary>
public static class ProjectionReadBody
{
    internal static ProjectionBatch ReadProjectionBatchRead(this DatabaseEngine engine, IKeyValueView view, string principalId, ReadProjectionBatchRequest request)
    {
        var now = engine.Clock.GetUtcNow();
        var principal = engine.Principal(view, principalId, now);
        ProjectionBatchExecution.RequireProjectionAdministrator(principal);
        if (request.Limit < ProjectionOutbox.MinimumProjectionReadCount || request.MaxBytes < ProjectionOutbox.MinimumProjectionReadCount)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ProjectionOutbox.ProjectionBatchBudgetDetail);
        }

        var state = ProjectionConsumerIdentity.ProjectionConsumer(view, request.Consumer);
        if (state.Released)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ProjectionOutbox.ReleasedProjectionGenerationDetail);
        }

        ChangeFeedsQueries.BindProjectionReadResources(view, request.Consumer.Partition, state.Definition.Resources);
        var head = engine.ReadOutboxHead(view, request.Consumer.Partition);
        ProjectionOutbox.CheckOutboxPosition(head, state.Checkpoint);
        var upper = ProjectionBatchExecution.ProjectionUpperSequence(request, state, head);
        var entries = new List<OutboxEntry>();
        var through = state.Checkpoint;
        var bytes = ProjectionOutbox.InitialProjectionPageBytes;
        foreach (var stored in StoredOutboxReader.ReadRange(view, request.Consumer.Partition, through, upper, request.Limit))
        {
            var entry = stored.Entry;
            if (!ProjectionOutbox.ProjectionAccepts(state.Definition, entry))
            {
                through = entry.Sequence;
                continue;
            }

            var size = stored.StoredBytes;
            if (size > request.MaxBytes - bytes)
            {
                if (through == state.Checkpoint)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, ProjectionOutbox.FirstProjectionEntryExceedsBudgetDetail);
                }

                break;
            }

            entries.Add(entry);
            bytes += size;
            through = entry.Sequence;
        }

        return new(state, entries.ToImmutableArray(), through, engine.Sign(new ProjectionBatchClaims(ProjectionOutbox.ProjectionBatchTokenPurpose, engine.Store.Identity.Incarnation, request.Consumer, state.Definition.IndexGeneration, state.Checkpoint, through, now.Add(engine.ChangeFeedExecution.ProjectionBatchLifetime))), through < upper);
    }

    internal static OutboxStatus GetOutboxStatusRead(this DatabaseEngine engine, IKeyValueView view, string principalId, PartitionRef partition)
    {
        ProjectionBatchExecution.RequireProjectionAdministrator(engine.Principal(view, principalId, engine.Clock.GetUtcNow()));
        EngineResourceAccess.ValidatePartition(partition);
        var head = engine.ReadOutboxHead(view, partition);
        var consumers = ProjectionConsumerIdentity.ProjectionConsumers(view, partition);
        foreach (var consumer in consumers)
        {
            ChangeFeedsQueries.BindProjectionReadResources(view, partition, consumer.Definition.Resources);
            ProjectionConsumerIdentity.RequireProjectionRetentionCheckpoint(consumer, head);
        }

        return new(head, consumers);
    }
}
